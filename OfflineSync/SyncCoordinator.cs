using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BamboeUp.Mobile.Shell.Services;

namespace BamboeUp.Mobile.OfflineSync;

public interface ISyncCoordinator
{
    bool IsOnline { get; }
    bool IsSyncing { get; }
    event Action? StateChanged;
    Task StartAsync();
    Task SyncNowAsync(CancellationToken cancellationToken = default);
}

public sealed class SyncCoordinator : ISyncCoordinator, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly IOfflineSyncStore _store;
    private readonly IHttpClientFactory _clients;
    private readonly MobileAuthService _auth;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private bool _started;

    public SyncCoordinator(IOfflineSyncStore store, IHttpClientFactory clients, MobileAuthService auth)
    {
        _store = store;
        _clients = clients;
        _auth = auth;
    }

    public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
    public bool IsSyncing { get; private set; }
    public event Action? StateChanged;

    public async Task StartAsync()
    {
        if (_started)
            return;
        _started = true;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        _auth.AuthStateChanged += OnAuthStateChanged;
        _store.Changed += OnStoreChanged;
        if (_auth.IsAuthenticated && await _store.GetCurrentScopeAsync() is not null)
            await _store.PurgeExpiredReceiptsAsync();
        if (IsOnline && _auth.IsAuthenticated)
            _ = SyncNowAsync();
    }

    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (!IsOnline || !_auth.IsAuthenticated || !await _syncGate.WaitAsync(0, cancellationToken))
            return;
        IsSyncing = true;
        StateChanged?.Invoke();
        try
        {
            foreach (var operation in await _store.GetReadyAsync(20, cancellationToken))
            {
                if (!IsOnline || !_auth.IsAuthenticated)
                    break;
                await UploadAsync(operation, cancellationToken);
            }
            await _store.PurgeExpiredReceiptsAsync();
        }
        finally
        {
            IsSyncing = false;
            _syncGate.Release();
            StateChanged?.Invoke();
        }
    }

    private async Task UploadAsync(SyncOperation operation, CancellationToken cancellationToken)
    {
        await _store.MarkUploadingAsync(operation.OperationId);
        try
        {
            using var form = new MultipartFormDataContent();
            var envelope = new SyncOperationEnvelope(
                Guid.Parse(operation.OperationId),
                Guid.Parse(operation.EntityGuid),
                operation.EntityType,
                operation.OperationType,
                operation.PayloadVersion,
                operation.PayloadHash,
                operation.PayloadJson);
            form.Add(new StringContent(JsonSerializer.Serialize(envelope, JsonOptions), Encoding.UTF8, "application/json"), "operation");
            if (!string.IsNullOrWhiteSpace(operation.BlobId))
            {
                var blob = await _store.GetBlobAsync(operation.BlobId)
                    ?? throw new InvalidOperationException("The staged evidence file is missing.");
                var stream = await _store.OpenBlobAsync(blob.BlobId, cancellationToken);
                var content = new StreamContent(stream);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(blob.ContentType);
                form.Add(content, "blob", blob.FileName);
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "api/sync/operations") { Content = form };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", operation.OperationId);
            using var response = await _clients.CreateClient("BamboeUp.Mobile.Authenticated")
                .SendAsync(request, cancellationToken);
            var receipt = await ReadReceiptAsync(response, cancellationToken);
            if (receipt is not null && receipt.OperationId.ToString("D").Equals(operation.OperationId, StringComparison.OrdinalIgnoreCase))
            {
                await _store.RecordReceiptAsync(receipt);
                return;
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                await _store.RecordReceiptAsync(new SyncServerReceipt(
                    Guid.Parse(operation.OperationId), SyncOperationStatus.Conflict,
                    "The server reported a conflict without a receipt.", null));
                return;
            }
            if ((int)response.StatusCode is >= 400 and < 500 && response.StatusCode is not HttpStatusCode.RequestTimeout and not HttpStatusCode.TooManyRequests)
            {
                await _store.RecordReceiptAsync(new SyncServerReceipt(
                    Guid.Parse(operation.OperationId), SyncOperationStatus.Rejected,
                    $"The server rejected the operation ({(int)response.StatusCode}).", null));
                return;
            }
            throw new HttpRequestException($"Sync did not return an explicit receipt ({(int)response.StatusCode}).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await _store.MarkRetryableAsync(operation.OperationId, "Sync timed out.");
        }
        catch (Exception ex)
        {
            await _store.MarkRetryableAsync(operation.OperationId, ex.Message);
        }
    }

    private static async Task<SyncServerReceipt?> ReadReceiptAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength == 0)
            return null;
        try
        {
            return await response.Content.ReadFromJsonAsync<SyncServerReceipt>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        StateChanged?.Invoke();
        if (e.NetworkAccess == NetworkAccess.Internet)
            _ = SyncNowAsync();
    }

    private void OnAuthStateChanged()
    {
        StateChanged?.Invoke();
        if (_auth.IsAuthenticated && IsOnline)
            _ = SyncNowAsync();
    }

    private void OnStoreChanged() => StateChanged?.Invoke();

    public void Dispose()
    {
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        _auth.AuthStateChanged -= OnAuthStateChanged;
        _store.Changed -= OnStoreChanged;
        _syncGate.Dispose();
    }
}
