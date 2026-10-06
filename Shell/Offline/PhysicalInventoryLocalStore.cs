using System.Security.Cryptography;
using BamboeUp.Mobile.OfflineSync;
using Shared.DataTransferObjects;
using SQLite;

namespace BamboeUp.Mobile.Shell.Offline;

public interface IPhysicalInventoryLocalStore
{
    event Action? Changed;
    bool IsOpen { get; }
    Task EnsureOpenedAsync();
    Task<LocalInventoryCount> CreateCountAsync(string documentNumber, int pageNumber, Guid counterUserGuid, Guid deviceGuid);
    Task<IReadOnlyList<PhysicalInventorySessionSummary>> ListAsync();
    Task<LocalInventoryCount?> GetAsync(string captureGuid);
    Task<IReadOnlyList<LocalInventoryCountLine>> GetLinesAsync(string captureGuid);
    Task<LocalInventoryCountLine> SaveItemAsync(string captureGuid, string barcode, decimal quantity, string source);
    Task UpdateLineQuantityAsync(string lineGuid, decimal quantity);
    Task FinishPageAsync(string captureGuid);
    Task MarkUploadingAsync(string captureGuid);
    Task MarkRetryableAsync(string captureGuid, string error);
    Task RecordReceiptAndPurgeAsync(PhysicalInventoryCountReceiptDto receipt);
    Task<IReadOnlyList<LocalInventoryCount>> GetReadyToUploadAsync();
    Task<PhysicalInventorySyncSnapshot> GetSnapshotAsync();
    Task PurgeExpiredReceiptsAsync();
}

/// <summary>
/// Encrypted SQLite store created on first New Physical Inventory Count, partitioned by user+company.
/// The DB key is random and stored in SecureStorage; it is never derived from the JWT.
/// </summary>
public sealed class PhysicalInventoryLocalStore : IPhysicalInventoryLocalStore
{
    private static int _sqliteInitialized;
    private readonly IOfflineStorageEnvironment _environment;
    private readonly Func<Task<SyncScope?>> _scope;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SQLiteAsyncConnection? _database;
    private SyncScope? _openScope;

    public PhysicalInventoryLocalStore(IOfflineStorageEnvironment environment, Func<Task<SyncScope?>> scope)
    {
        _environment = environment;
        _scope = scope;
    }

    public event Action? Changed;
    public bool IsOpen => _database is not null;

    public Task EnsureOpenedAsync() => GetDatabaseAsync();

    public async Task<LocalInventoryCount> CreateCountAsync(
        string documentNumber, int pageNumber, Guid counterUserGuid, Guid deviceGuid)
    {
        if (!PhysicalInventoryLocalRules.TryValidateHeader(documentNumber, pageNumber, out var error))
            throw new ArgumentException(error);

        var db = await GetDatabaseAsync();
        var now = DateTime.UtcNow;
        var row = new LocalInventoryCount
        {
            CaptureGuid = Guid.NewGuid().ToString("D"),
            OperationId = Guid.NewGuid().ToString("D"),
            DocumentNumber = documentNumber.Trim(),
            PageNumber = pageNumber,
            CounterUserGuid = counterUserGuid.ToString("D"),
            DeviceGuid = deviceGuid.ToString("D"),
            StartedUtc = now,
            Status = PhysicalInventoryStatuses.InProgress,
            UpdatedUtc = now
        };
        await db.InsertAsync(row);
        Changed?.Invoke();
        return row;
    }

    public async Task<IReadOnlyList<PhysicalInventorySessionSummary>> ListAsync()
    {
        if (!await CanOpenAsync())
            return [];
        var db = await GetDatabaseAsync();
        var headers = await db.Table<LocalInventoryCount>().OrderByDescending(x => x.UpdatedUtc).ToListAsync();
        var lines = await db.Table<LocalInventoryCountLine>().ToListAsync();
        var receipts = await db.Table<LocalSyncReceipt>().ToListAsync();
        var list = new List<PhysicalInventorySessionSummary>();
        foreach (var header in headers)
        {
            var captureLines = lines.Where(x => x.CaptureGuid == header.CaptureGuid).ToList();
            list.Add(new PhysicalInventorySessionSummary(
                header.CaptureGuid,
                header.DocumentNumber,
                header.PageNumber,
                header.Status,
                captureLines.Count,
                captureLines.Sum(x => x.Quantity),
                header.UpdatedUtc,
                header.LastError));
        }

        foreach (var receipt in receipts.OrderByDescending(x => x.CommittedUtc))
        {
            if (list.Any(x => x.CaptureGuid == receipt.CaptureGuid))
                continue;
            list.Add(new PhysicalInventorySessionSummary(
                receipt.CaptureGuid,
                string.Empty,
                0,
                PhysicalInventoryStatuses.UploadedPendingVerification,
                0,
                0,
                receipt.CommittedUtc,
                receipt.ServerMessage));
        }

        return list;
    }

    public async Task<LocalInventoryCount?> GetAsync(string captureGuid)
    {
        var db = await GetDatabaseAsync();
        return await db.FindAsync<LocalInventoryCount>(captureGuid);
    }

    public async Task<IReadOnlyList<LocalInventoryCountLine>> GetLinesAsync(string captureGuid)
    {
        var db = await GetDatabaseAsync();
        return await db.Table<LocalInventoryCountLine>()
            .Where(x => x.CaptureGuid == captureGuid)
            .OrderBy(x => x.Barcode)
            .ToListAsync();
    }

    public async Task<LocalInventoryCountLine> SaveItemAsync(
        string captureGuid, string barcode, decimal quantity, string source)
    {
        if (!PhysicalInventoryLocalRules.TryValidateLine(barcode, quantity, out var error))
            throw new ArgumentException(error);

        var db = await GetDatabaseAsync();
        var header = await db.FindAsync<LocalInventoryCount>(captureGuid)
            ?? throw new InvalidOperationException("The count session was not found.");
        if (!string.Equals(header.Status, PhysicalInventoryStatuses.InProgress, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Finished pages cannot accept more items.");

        var normalized = PhysicalInventoryLocalRules.NormalizeBarcode(barcode);
        var now = DateTime.UtcNow;
        await db.RunInTransactionAsync(conn =>
        {
            var existing = conn.Table<LocalInventoryCountLine>()
                .FirstOrDefault(x => x.CaptureGuid == captureGuid && x.Barcode == normalized);
            if (existing is null)
            {
                conn.Insert(new LocalInventoryCountLine
                {
                    LineGuid = Guid.NewGuid().ToString("D"),
                    CaptureGuid = captureGuid,
                    Barcode = normalized,
                    Quantity = quantity,
                    ScanCount = 1,
                    FirstCapturedUtc = now,
                    LastCapturedUtc = now,
                    Source = source
                });
            }
            else
            {
                existing.Quantity = PhysicalInventoryLocalRules.Accumulate(existing.Quantity, quantity);
                existing.ScanCount += 1;
                existing.LastCapturedUtc = now;
                if (!string.Equals(existing.Source, source, StringComparison.OrdinalIgnoreCase))
                    existing.Source = "Mixed";
                conn.Update(existing);
            }

            header.UpdatedUtc = now;
            conn.Update(header);
        });

        Changed?.Invoke();
        var saved = (await GetLinesAsync(captureGuid)).First(x => x.Barcode == normalized);
        return saved;
    }

    public async Task UpdateLineQuantityAsync(string lineGuid, decimal quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero.");
        var db = await GetDatabaseAsync();
        var line = await db.FindAsync<LocalInventoryCountLine>(lineGuid)
            ?? throw new InvalidOperationException("The line was not found.");
        line.Quantity = quantity;
        line.LastCapturedUtc = DateTime.UtcNow;
        await db.UpdateAsync(line);
        Changed?.Invoke();
    }

    public async Task FinishPageAsync(string captureGuid)
    {
        var db = await GetDatabaseAsync();
        var header = await db.FindAsync<LocalInventoryCount>(captureGuid)
            ?? throw new InvalidOperationException("The count session was not found.");
        var lines = await GetLinesAsync(captureGuid);
        if (lines.Count == 0)
            throw new InvalidOperationException("Finish Page requires at least one counted item.");
        header.Status = PhysicalInventoryStatuses.PendingUpload;
        header.FinishedUtc = DateTime.UtcNow;
        header.UpdatedUtc = header.FinishedUtc.Value;
        header.LastError = null;
        await db.UpdateAsync(header);
        Changed?.Invoke();
    }

    public async Task MarkUploadingAsync(string captureGuid)
    {
        var db = await GetDatabaseAsync();
        var header = await db.FindAsync<LocalInventoryCount>(captureGuid);
        if (header is null)
            return;
        header.Status = PhysicalInventoryStatuses.Uploading;
        header.UpdatedUtc = DateTime.UtcNow;
        await db.UpdateAsync(header);
        Changed?.Invoke();
    }

    public async Task MarkRetryableAsync(string captureGuid, string error)
    {
        var db = await GetDatabaseAsync();
        var header = await db.FindAsync<LocalInventoryCount>(captureGuid);
        if (header is null)
            return;
        header.Status = PhysicalInventoryStatuses.PendingUpload;
        header.LastError = error;
        header.UpdatedUtc = DateTime.UtcNow;
        await db.UpdateAsync(header);
        Changed?.Invoke();
    }

    public async Task RecordReceiptAndPurgeAsync(PhysicalInventoryCountReceiptDto receipt)
    {
        var db = await GetDatabaseAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.InsertOrReplace(new LocalSyncReceipt
            {
                OperationId = receipt.OperationId.ToString("D"),
                CaptureGuid = receipt.CaptureGuid.ToString("D"),
                Status = receipt.Status,
                PayloadHash = receipt.PayloadHash,
                ServerMessage = receipt.Message,
                ConcurrencyToken = receipt.ConcurrencyToken,
                CommittedUtc = receipt.CommittedAt.UtcDateTime
            });

            var keepPayload = string.Equals(receipt.Code, "PAYLOAD_MISMATCH", StringComparison.Ordinal)
                || string.Equals(receipt.Status, PhysicalInventoryStatuses.Conflict, StringComparison.OrdinalIgnoreCase)
                || string.Equals(receipt.Status, PhysicalInventoryStatuses.Rejected, StringComparison.OrdinalIgnoreCase);
            if (keepPayload)
            {
                var header = conn.Find<LocalInventoryCount>(receipt.CaptureGuid.ToString("D"));
                if (header is not null)
                {
                    header.Status = string.IsNullOrWhiteSpace(receipt.Status)
                        ? PhysicalInventoryStatuses.Conflict
                        : receipt.Status;
                    header.LastError = receipt.Message;
                    header.UpdatedUtc = DateTime.UtcNow;
                    conn.Update(header);
                }
                return;
            }

            conn.Execute("DELETE FROM local_inventory_count_lines WHERE CaptureGuid = ?", receipt.CaptureGuid.ToString("D"));
            conn.Execute("DELETE FROM local_inventory_counts WHERE CaptureGuid = ?", receipt.CaptureGuid.ToString("D"));
        });
        Changed?.Invoke();
    }

    public async Task<IReadOnlyList<LocalInventoryCount>> GetReadyToUploadAsync()
    {
        if (!await CanOpenAsync())
            return [];
        var db = await GetDatabaseAsync();
        return await db.Table<LocalInventoryCount>()
            .Where(x => x.Status == PhysicalInventoryStatuses.PendingUpload
                        || x.Status == PhysicalInventoryStatuses.Uploading)
            .ToListAsync();
    }

    public async Task<PhysicalInventorySyncSnapshot> GetSnapshotAsync()
    {
        if (!await CanOpenAsync())
            return new PhysicalInventorySyncSnapshot(0, 0, 0, 0, 0, 0);
        var db = await GetDatabaseAsync();
        var rows = await db.Table<LocalInventoryCount>().ToListAsync();
        var receipts = await db.Table<LocalSyncReceipt>().ToListAsync();
        return new PhysicalInventorySyncSnapshot(
            rows.Count(x => x.Status == PhysicalInventoryStatuses.InProgress),
            rows.Count(x => x.Status == PhysicalInventoryStatuses.PendingUpload),
            rows.Count(x => x.Status == PhysicalInventoryStatuses.Uploading),
            receipts.Count,
            rows.Count(x => x.Status == PhysicalInventoryStatuses.Conflict),
            rows.Count(x => x.Status == PhysicalInventoryStatuses.Rejected));
    }

    public async Task PurgeExpiredReceiptsAsync()
    {
        if (!await CanOpenAsync())
            return;
        var db = await GetDatabaseAsync();
        var cutoff = DateTime.UtcNow.AddDays(-PhysicalInventoryApps.ReceiptRetentionDays);
        await db.Table<LocalSyncReceipt>().DeleteAsync(x => x.CommittedUtc < cutoff);
    }

    private async Task<bool> CanOpenAsync()
    {
        var scope = await _scope();
        return scope is not null;
    }

    private async Task<SQLiteAsyncConnection> GetDatabaseAsync()
    {
        var scope = await _scope()
            ?? throw new InvalidOperationException("A user and company session is required for the inventory store.");
        if (_database is not null && scope == _openScope)
            return _database;

        await _gate.WaitAsync();
        try
        {
            if (_database is not null && scope == _openScope)
                return _database;
            if (Interlocked.Exchange(ref _sqliteInitialized, 1) == 0)
                SQLitePCL.Batteries_V2.Init();

            var root = Path.Combine(_environment.AppDataDirectory, "offline", scope.StableId);
            Directory.CreateDirectory(root);
            var keyName = $"bamboeup_inventory_db_key_{scope.StableId}";
            var storedKey = await _environment.GetSecureAsync(keyName);
            if (string.IsNullOrWhiteSpace(storedKey))
            {
                storedKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                await _environment.SetSecureAsync(keyName, storedKey);
            }

            var flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex;
            var connection = new SQLiteConnectionString(
                Path.Combine(root, "physical-inventory.db3"), flags, storeDateTimeAsTicks: true, key: storedKey);
            _database = new SQLiteAsyncConnection(connection);
            await _database.CreateTableAsync<LocalInventoryCount>();
            await _database.CreateTableAsync<LocalInventoryCountLine>();
            await _database.CreateTableAsync<LocalSyncReceipt>();
            await _database.ExecuteAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_local_inventory_count_lines_capture_barcode ON local_inventory_count_lines (CaptureGuid, Barcode)");
            await _database.ExecuteAsync(
                "UPDATE local_inventory_counts SET Status = ?, LastError = ?, UpdatedUtc = ? WHERE Status = ?",
                PhysicalInventoryStatuses.PendingUpload, "The previous upload was interrupted.", DateTime.UtcNow,
                PhysicalInventoryStatuses.Uploading);
            _openScope = scope;
            return _database;
        }
        finally
        {
            _gate.Release();
        }
    }
}
