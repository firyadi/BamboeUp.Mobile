using System.Net.Http.Json;
using Shared.DataTransferObjects;

namespace BamboeUp.Mobile.Modules.Services;

public sealed class HospitalApi
{
    private readonly HttpClient _http;

    public HospitalApi(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<HospitalDto>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("api/hospitals", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<HospitalDto>>(cancellationToken) ?? [];
    }

    public async Task<HospitalDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/hospitals/{id}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<HospitalDto>(cancellationToken);
    }

    public async Task CreateAsync(HospitalForCreationDto dto, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("api/hospitals", dto, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await Fail(response, "Save failed", cancellationToken);
    }

    public async Task UpdateAsync(Guid id, HospitalForUpdateDto dto, CancellationToken cancellationToken = default)
    {
        var response = await _http.PutAsJsonAsync($"api/hospitals/{id}", dto, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await Fail(response, "Save failed", cancellationToken);
    }

    private static async Task<HttpRequestException> Fail(HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new HttpRequestException(string.IsNullOrWhiteSpace(body)
            ? $"{fallback} ({(int)response.StatusCode})."
            : body);
    }
}
