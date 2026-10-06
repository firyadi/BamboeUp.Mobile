using System.Net.Http.Json;
using Shared.DataTransferObjects;

namespace BamboeUp.Mobile.Shell.Services;

public interface IPhysicalInventoryApi
{
    Task<string?> GetPublicKeyAsync(CancellationToken cancellationToken = default);
    Task<OfflineAccessPermitDto?> IssuePermitAsync(Guid deviceGuid, CancellationToken cancellationToken = default);
    Task<PhysicalInventoryCountReceiptDto?> UploadAsync(PhysicalInventoryCountUploadDto upload, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhysicalInventoryCaptureDto>> GetQueueAsync(CancellationToken cancellationToken = default);
    Task<PhysicalInventoryCaptureDto?> GetCaptureAsync(Guid captureGuid, CancellationToken cancellationToken = default);
    Task VerifyAsync(Guid captureGuid, PhysicalInventoryVerifyRequestDto input, CancellationToken cancellationToken = default);
    Task RejectAsync(Guid captureGuid, PhysicalInventoryVerifyRequestDto input, CancellationToken cancellationToken = default);
}

public sealed class PhysicalInventoryApi(IHttpClientFactory factory) : IPhysicalInventoryApi
{
    private HttpClient Client => factory.CreateClient("BamboeUp.Mobile.Authenticated");

    public async Task<string?> GetPublicKeyAsync(CancellationToken cancellationToken = default)
    {
        var response = await Client.GetAsync("api/v1/mobile-sync/offline-permit/public-key", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        var payload = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: cancellationToken);
        return payload.TryGetProperty("publicKey", out var key) ? key.GetString() : null;
    }

    public async Task<OfflineAccessPermitDto?> IssuePermitAsync(Guid deviceGuid, CancellationToken cancellationToken = default)
    {
        var response = await Client.PostAsJsonAsync(
            "api/v1/mobile-sync/offline-permit",
            new OfflinePermitIssueRequest { DeviceGuid = deviceGuid },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Offline permit failed ({(int)response.StatusCode}): {error}");
        }

        return await response.Content.ReadFromJsonAsync<OfflineAccessPermitDto>(cancellationToken: cancellationToken);
    }

    public async Task<PhysicalInventoryCountReceiptDto?> UploadAsync(
        PhysicalInventoryCountUploadDto upload, CancellationToken cancellationToken = default)
    {
        var response = await Client.PostAsJsonAsync(
            "api/v1/mobile-sync/physical-inventory-counts", upload, cancellationToken);
        var receipt = await response.Content.ReadFromJsonAsync<PhysicalInventoryCountReceiptDto>(cancellationToken: cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            return receipt;
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Upload failed ({(int)response.StatusCode}): {error}");
        }

        return receipt;
    }

    public async Task<IReadOnlyList<PhysicalInventoryCaptureDto>> GetQueueAsync(CancellationToken cancellationToken = default)
    {
        var response = await Client.GetAsync("api/v1/mobile-sync/physical-inventory-counts", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<PhysicalInventoryCaptureDto>>(cancellationToken) ?? [];
    }

    public async Task<PhysicalInventoryCaptureDto?> GetCaptureAsync(Guid captureGuid, CancellationToken cancellationToken = default)
    {
        var response = await Client.GetAsync(
            $"api/v1/mobile-sync/physical-inventory-counts/{captureGuid:D}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<PhysicalInventoryCaptureDto>(cancellationToken);
    }

    public async Task VerifyAsync(Guid captureGuid, PhysicalInventoryVerifyRequestDto input, CancellationToken cancellationToken = default)
    {
        var response = await Client.PostAsJsonAsync(
            $"api/v1/mobile-sync/physical-inventory-counts/{captureGuid:D}/verify", input, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Verify failed ({(int)response.StatusCode}): {error}");
        }
    }

    public async Task RejectAsync(Guid captureGuid, PhysicalInventoryVerifyRequestDto input, CancellationToken cancellationToken = default)
    {
        var response = await Client.PostAsJsonAsync(
            $"api/v1/mobile-sync/physical-inventory-counts/{captureGuid:D}/reject", input, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Reject failed ({(int)response.StatusCode}): {error}");
        }
    }
}
