using System.Net.Http.Json;
using Shared.DataTransferObjects;

namespace BamboeUp.Mobile.Modules.Services;

public sealed class OvertimeRequestApi
{
    private readonly HttpClient _http;

    public OvertimeRequestApi(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<OvertimeRequestDto>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("api/overtimeRequests", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<OvertimeRequestDto>>(cancellationToken) ?? [];
    }

    public async Task<OvertimeRequestDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/overtimeRequests/{id}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<OvertimeRequestDto>(cancellationToken);
    }

    public async Task CreateAsync(OvertimeRequestForCreationDto dto, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("api/overtimeRequests", dto, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(string.IsNullOrWhiteSpace(body)
                ? $"Save failed ({(int)response.StatusCode})."
                : body);
        }
    }

    public async Task DeleteAsync(Guid id, long deletedById, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"api/overtimeRequests/{id}")
        {
            Content = JsonContent.Create(new OvertimeRequestForDeleteDto { DeletedById = deletedById })
        };
        var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(string.IsNullOrWhiteSpace(body)
                ? $"Delete failed ({(int)response.StatusCode})."
                : body);
        }
    }

    public async Task UpdateAsync(Guid id, OvertimeRequestForUpdateDto dto, CancellationToken cancellationToken = default)
    {
        var response = await _http.PutAsJsonAsync($"api/overtimeRequests/{id}", dto, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(string.IsNullOrWhiteSpace(body)
                ? $"Save failed ({(int)response.StatusCode})."
                : body);
        }
    }

    public async Task<IReadOnlyList<PersonDto>> GetPeopleAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("api/people", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<PersonDto>>(cancellationToken) ?? [];
    }
}
