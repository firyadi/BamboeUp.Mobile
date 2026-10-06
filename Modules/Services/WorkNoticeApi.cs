using System.Net.Http.Json;
using Shared.DataTransferObjects;

namespace BamboeUp.Mobile.Modules.Services;

public sealed class WorkNoticeApi
{
    private readonly HttpClient _http;

    public WorkNoticeApi(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<WorkNoticeDto>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("api/workNotices", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<WorkNoticeDto>>(cancellationToken) ?? [];
    }

    public async Task<WorkNoticeDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/workNotices/{id}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<WorkNoticeDto>(cancellationToken);
    }

    public async Task CreateAsync(WorkNoticeForCreationDto dto, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("api/workNotices", dto, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await Fail(response, cancellationToken);
    }

    public async Task UpdateAsync(Guid id, WorkNoticeForUpdateDto dto, CancellationToken cancellationToken = default)
    {
        var response = await _http.PutAsJsonAsync($"api/workNotices/{id}", dto, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await Fail(response, cancellationToken);
    }

    private static async Task<HttpRequestException> Fail(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new HttpRequestException(string.IsNullOrWhiteSpace(body)
            ? $"Save failed ({(int)response.StatusCode})."
            : body);
    }
}
