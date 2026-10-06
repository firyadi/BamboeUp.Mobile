using BamboeUp.Mobile.Shell.Device;
using BamboeUp.Mobile.Shell.Services;
using Shared.DataTransferObjects;

namespace BamboeUp.Mobile.Shell.Offline;

public interface IPhysicalInventorySyncCoordinator
{
    bool IsOnline { get; }
    bool IsWifi { get; }
    bool IsSyncing { get; }
    event Action? StateChanged;
    Task StartAsync();
    Task SyncFinishedPagesAsync(bool wifiOnly, CancellationToken cancellationToken = default);
    Task UploadNowAsync(bool confirmNonWifi, CancellationToken cancellationToken = default);
}

public sealed class PhysicalInventorySyncCoordinator : IPhysicalInventorySyncCoordinator, IDisposable
{
    private readonly IPhysicalInventoryLocalStore _store;
    private readonly IPhysicalInventoryApi _api;
    private readonly MobileAuthService _auth;
    private readonly IDeviceServices _device;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;

    public PhysicalInventorySyncCoordinator(
        IPhysicalInventoryLocalStore store,
        IPhysicalInventoryApi api,
        MobileAuthService auth,
        IDeviceServices device)
    {
        _store = store;
        _api = api;
        _auth = auth;
        _device = device;
    }

    public bool IsOnline => PhysicalInventoryDeviceProfile.HasInternet();
    public bool IsWifi => PhysicalInventoryDeviceProfile.IsWifiConnected();
    public bool IsSyncing { get; private set; }
    public event Action? StateChanged;

    public async Task StartAsync()
    {
        if (_started)
            return;
        _started = true;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        _auth.AuthStateChanged += OnAuthChanged;
        _store.Changed += OnStoreChanged;
        if (_auth.CanCapturePhysicalInventory)
            await _store.PurgeExpiredReceiptsAsync();
        if (IsOnline && IsWifi && _auth.IsOnlineAuthenticated)
            _ = SyncFinishedPagesAsync(wifiOnly: true);
    }

    public Task UploadNowAsync(bool confirmNonWifi, CancellationToken cancellationToken = default)
    {
        if (!IsWifi && !confirmNonWifi)
            throw new InvalidOperationException("Uploading off Wi-Fi requires confirmation.");
        return SyncFinishedPagesAsync(wifiOnly: false, cancellationToken);
    }

    public async Task SyncFinishedPagesAsync(bool wifiOnly, CancellationToken cancellationToken = default)
    {
        if (!IsOnline || !_auth.IsOnlineAuthenticated)
            return;
        if (wifiOnly && !IsWifi)
            return;
        if (!await _gate.WaitAsync(0, cancellationToken))
            return;

        IsSyncing = true;
        StateChanged?.Invoke();
        try
        {
            try
            {
                await _auth.RefreshPermitAsync(await _device.GetDeviceGuidAsync());
            }
            catch
            {
                return;
            }

            if (_auth.OfflinePermit is { CanCapture: false })
                return;

            foreach (var header in await _store.GetReadyToUploadAsync())
            {
                if (!IsOnline || !_auth.IsOnlineAuthenticated)
                    break;
                if (wifiOnly && !IsWifi)
                    break;
                await UploadOneAsync(header, cancellationToken);
            }

            await _store.PurgeExpiredReceiptsAsync();
        }
        finally
        {
            IsSyncing = false;
            _gate.Release();
            StateChanged?.Invoke();
        }
    }

    private async Task UploadOneAsync(LocalInventoryCount header, CancellationToken cancellationToken)
    {
        await _store.MarkUploadingAsync(header.CaptureGuid);
        try
        {
            var lines = await _store.GetLinesAsync(header.CaptureGuid);
            var upload = new PhysicalInventoryCountUploadDto
            {
                OperationId = Guid.Parse(header.OperationId),
                CaptureGuid = Guid.Parse(header.CaptureGuid),
                DeviceGuid = Guid.Parse(header.DeviceGuid),
                DocumentNumber = header.DocumentNumber,
                PageNumber = header.PageNumber,
                StartedAt = new DateTimeOffset(DateTime.SpecifyKind(header.StartedUtc, DateTimeKind.Utc)),
                FinishedAt = new DateTimeOffset(DateTime.SpecifyKind(header.FinishedUtc ?? DateTime.UtcNow, DateTimeKind.Utc)),
                PayloadHash = string.Empty,
                Lines = lines.Select(x => new PhysicalInventoryCountLineUploadDto
                {
                    LineGuid = Guid.Parse(x.LineGuid),
                    Barcode = x.Barcode,
                    Quantity = x.Quantity,
                    ScanCount = x.ScanCount,
                    FirstCapturedAt = new DateTimeOffset(DateTime.SpecifyKind(x.FirstCapturedUtc, DateTimeKind.Utc)),
                    LastCapturedAt = new DateTimeOffset(DateTime.SpecifyKind(x.LastCapturedUtc, DateTimeKind.Utc)),
                    Source = x.Source
                }).ToList()
            };
            upload = upload with { PayloadHash = PhysicalInventoryPayloadHasher.Compute(upload) };

            var receipt = await _api.UploadAsync(upload, cancellationToken)
                ?? throw new InvalidOperationException("The server did not return a receipt.");
            await _store.RecordReceiptAndPurgeAsync(receipt);
        }
        catch (Exception ex)
        {
            await _store.MarkRetryableAsync(header.CaptureGuid, ex.Message);
        }
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        StateChanged?.Invoke();
        if (e.NetworkAccess == NetworkAccess.Internet && PhysicalInventoryDeviceProfile.IsWifiConnected())
            _ = SyncFinishedPagesAsync(wifiOnly: true);
    }

    private void OnAuthChanged()
    {
        StateChanged?.Invoke();
        if (_auth.IsOnlineAuthenticated && IsWifi)
            _ = SyncFinishedPagesAsync(wifiOnly: true);
    }

    private void OnStoreChanged() => StateChanged?.Invoke();

    public void Dispose()
    {
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        _auth.AuthStateChanged -= OnAuthChanged;
        _store.Changed -= OnStoreChanged;
        _gate.Dispose();
    }
}
