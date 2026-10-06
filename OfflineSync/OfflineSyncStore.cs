using System.Security.Cryptography;
using System.Text;
using BamboeUp.Mobile.Shell.Services;
using SQLite;

namespace BamboeUp.Mobile.OfflineSync;

public interface IOfflineSyncStore
{
    event Action? Changed;
    Task<SyncScope?> GetCurrentScopeAsync();
    Task<StagedBlob> StageBlobAsync(Stream source, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> OpenBlobAsync(string blobId, CancellationToken cancellationToken = default);
    Task<SyncBlob?> GetBlobAsync(string blobId);
    Task EnqueueAsync(SyncOperation operation);
    Task<IReadOnlyList<SyncOperation>> GetReadyAsync(int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SyncOperation>> GetOutstandingAsync(CancellationToken cancellationToken = default);
    Task MarkUploadingAsync(string operationId);
    Task RecordReceiptAsync(SyncServerReceipt receipt);
    Task MarkRetryableAsync(string operationId, string error);
    Task<SyncQueueSummary> GetSummaryAsync();
    Task PurgeExpiredReceiptsAsync();
}

public sealed class OfflineSyncStore : IOfflineSyncStore
{
    private static int _sqliteInitialized;
    private readonly MobileAuthService _auth;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SQLiteAsyncConnection? _database;
    private SyncScope? _openScope;
    private byte[]? _encryptionKey;
    private string? _blobDirectory;

    public OfflineSyncStore(MobileAuthService auth) => _auth = auth;

    public Task<SyncScope?> GetCurrentScopeAsync()
    {
        var user = _auth.CurrentUser;
        if (user is null || user.UserGuid == Guid.Empty || user.ActiveCompanyGuid is not Guid company || company == Guid.Empty)
            return Task.FromResult<SyncScope?>(null);
        return Task.FromResult<SyncScope?>(new SyncScope(user.UserGuid, company));
    }

    public async Task<StagedBlob> StageBlobAsync(Stream source, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var db = await GetDatabaseAsync();
        var blobId = Guid.NewGuid().ToString("N");
        var path = BlobPath(blobId);
        await using var plain = new MemoryStream();
        await source.CopyToAsync(plain, cancellationToken);
        var encrypted = Encrypt(plain.ToArray(), _encryptionKey!);
        await File.WriteAllBytesAsync(path, encrypted, cancellationToken);
        var row = new SyncBlob
        {
            BlobId = blobId,
            FileName = string.IsNullOrWhiteSpace(fileName) ? $"{blobId}.jpg" : Path.GetFileName(fileName),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            Length = plain.Length,
            CreatedUtc = DateTime.UtcNow
        };
        await db.InsertAsync(row);
        return new StagedBlob(row.BlobId, row.FileName, row.ContentType, row.Length);
    }

    public async Task<Stream> OpenBlobAsync(string blobId, CancellationToken cancellationToken = default)
    {
        _ = await GetDatabaseAsync();
        var encrypted = await File.ReadAllBytesAsync(BlobPath(blobId), cancellationToken);
        return new MemoryStream(Decrypt(encrypted, _encryptionKey!), writable: false);
    }

    public async Task<SyncBlob?> GetBlobAsync(string blobId)
    {
        var db = await GetDatabaseAsync();
        return await db.FindAsync<SyncBlob>(blobId);
    }

    public async Task EnqueueAsync(SyncOperation operation)
    {
        var db = await GetDatabaseAsync();
        operation.Status = SyncOperationStatus.Pending;
        operation.CreatedUtc = operation.CreatedUtc == default ? DateTime.UtcNow : operation.CreatedUtc;
        operation.UpdatedUtc = DateTime.UtcNow;
        await db.InsertOrReplaceAsync(operation);
        Changed?.Invoke();
    }

    public async Task<IReadOnlyList<SyncOperation>> GetReadyAsync(int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = await GetDatabaseAsync();
        var now = DateTime.UtcNow;
        return await db.Table<SyncOperation>()
            .Where(x => (x.StatusValue == (int)SyncOperationStatus.Pending ||
                         x.StatusValue == (int)SyncOperationStatus.Retryable) &&
                        (x.NextAttemptUtc == null || x.NextAttemptUtc <= now))
            .OrderBy(x => x.CreatedUtc)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<SyncOperation>> GetOutstandingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = await GetDatabaseAsync();
        return await db.Table<SyncOperation>()
            .Where(x => x.StatusValue != (int)SyncOperationStatus.Applied)
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync();
    }

    public Task MarkUploadingAsync(string operationId) =>
        UpdateOperationAsync(operationId, operation =>
        {
            operation.Status = SyncOperationStatus.Uploading;
            operation.AttemptCount++;
            operation.LastError = null;
        });

    public async Task RecordReceiptAsync(SyncServerReceipt receipt)
    {
        var db = await GetDatabaseAsync();
        var operationId = receipt.OperationId.ToString("D");
        var operation = await db.FindAsync<SyncOperation>(operationId);
        if (operation is null)
            return;

        operation.Status = receipt.Status;
        operation.LastError = receipt.Message;
        operation.UpdatedUtc = DateTime.UtcNow;
        await db.InsertOrReplaceAsync(new SyncReceipt
        {
            OperationId = operationId,
            Status = receipt.Status,
            ReceivedUtc = DateTime.UtcNow,
            ServerMessage = receipt.Message,
            ServerVersion = receipt.ServerVersion
        });

        if (receipt.Status == SyncOperationStatus.Applied)
        {
            await db.DeleteAsync(operation);
            await DeleteBlobAsync(db, operation.BlobId);
        }
        else
        {
            await db.UpdateAsync(operation);
        }
        Changed?.Invoke();
    }

    public Task MarkRetryableAsync(string operationId, string error) =>
        UpdateOperationAsync(operationId, operation =>
        {
            operation.Status = SyncOperationStatus.Retryable;
            operation.LastError = error;
            var seconds = Math.Min(900, 5 * Math.Pow(2, Math.Min(operation.AttemptCount, 8)));
            operation.NextAttemptUtc = DateTime.UtcNow.AddSeconds(seconds);
        });

    public async Task<SyncQueueSummary> GetSummaryAsync()
    {
        var scope = await GetCurrentScopeAsync();
        if (scope is null)
            return new SyncQueueSummary(0, 0, 0, 0, 0);
        var db = await GetDatabaseAsync();
        var rows = await db.Table<SyncOperation>().ToListAsync();
        return new SyncQueueSummary(
            rows.Count(x => x.Status == SyncOperationStatus.Pending),
            rows.Count(x => x.Status == SyncOperationStatus.Uploading),
            rows.Count(x => x.Status == SyncOperationStatus.Retryable),
            rows.Count(x => x.Status == SyncOperationStatus.Conflict),
            rows.Count(x => x.Status == SyncOperationStatus.Rejected));
    }

    public async Task PurgeExpiredReceiptsAsync()
    {
        var db = await GetDatabaseAsync();
        var cutoff = DateTime.UtcNow.AddDays(-30);
        await db.Table<SyncReceipt>().DeleteAsync(x => x.ReceivedUtc < cutoff);
    }

    public event Action? Changed;

    private async Task UpdateOperationAsync(string operationId, Action<SyncOperation> update)
    {
        var db = await GetDatabaseAsync();
        var operation = await db.FindAsync<SyncOperation>(operationId);
        if (operation is null)
            return;
        update(operation);
        operation.UpdatedUtc = DateTime.UtcNow;
        await db.UpdateAsync(operation);
        Changed?.Invoke();
    }

    private async Task<SQLiteAsyncConnection> GetDatabaseAsync()
    {
        var scope = await GetCurrentScopeAsync() ?? throw new InvalidOperationException("A user and company session is required for offline storage.");
        if (_database is not null && scope == _openScope)
            return _database;

        await _gate.WaitAsync();
        try
        {
            if (_database is not null && scope == _openScope)
                return _database;
            if (Interlocked.Exchange(ref _sqliteInitialized, 1) == 0)
                SQLitePCL.Batteries_V2.Init();

            var root = Path.Combine(FileSystem.AppDataDirectory, "offline", scope.StableId);
            Directory.CreateDirectory(root);
            _blobDirectory = Path.Combine(root, "blobs");
            Directory.CreateDirectory(_blobDirectory);
            var keyName = $"bamboeup_sync_key_{scope.StableId}";
            var storedKey = await SecureStorage.Default.GetAsync(keyName);
            if (string.IsNullOrWhiteSpace(storedKey))
            {
                storedKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                await SecureStorage.Default.SetAsync(keyName, storedKey);
            }
            _encryptionKey = Convert.FromBase64String(storedKey);
            var flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex;
            var connection = new SQLiteConnectionString(
                Path.Combine(root, "sync.db3"), flags, storeDateTimeAsTicks: true, key: storedKey);
            _database = new SQLiteAsyncConnection(connection);
            await _database.CreateTableAsync<SyncOperation>();
            await _database.CreateTableAsync<SyncReceipt>();
            await _database.CreateTableAsync<SyncBlob>();
            _openScope = scope;
            await _database.ExecuteAsync(
                "UPDATE sync_operations SET StatusValue = ?, UpdatedUtc = ?, LastError = ? WHERE StatusValue = ?",
                (int)SyncOperationStatus.Retryable, DateTime.UtcNow,
                "The previous sync was interrupted.", (int)SyncOperationStatus.Uploading);
            var safelyApplied = await _database.QueryAsync<SyncOperation>(
                "SELECT o.* FROM sync_operations o INNER JOIN sync_receipts r ON r.OperationId = o.OperationId WHERE r.StatusValue = ?",
                (int)SyncOperationStatus.Applied);
            foreach (var operation in safelyApplied)
            {
                await _database.DeleteAsync(operation);
                await DeleteBlobAsync(_database, operation.BlobId);
            }
            return _database;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DeleteBlobAsync(SQLiteAsyncConnection db, string? blobId)
    {
        if (string.IsNullOrWhiteSpace(blobId))
            return;
        var blob = await db.FindAsync<SyncBlob>(blobId);
        if (blob is not null)
            await db.DeleteAsync(blob);
        var path = BlobPath(blobId);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string BlobPath(string blobId) =>
        Path.Combine(_blobDirectory ?? throw new InvalidOperationException("Offline store is not initialized."), $"{blobId}.blob");

    private static byte[] Encrypt(byte[] plain, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Encoding.ASCII.GetBytes("BUP1").Concat(nonce).Concat(tag).Concat(cipher).ToArray();
    }

    private static byte[] Decrypt(byte[] encrypted, byte[] key)
    {
        if (encrypted.Length < 32 || Encoding.ASCII.GetString(encrypted, 0, 4) != "BUP1")
            throw new CryptographicException("Invalid staged blob.");
        var nonce = encrypted.AsSpan(4, 12);
        var tag = encrypted.AsSpan(16, 16);
        var cipher = encrypted.AsSpan(32);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}
