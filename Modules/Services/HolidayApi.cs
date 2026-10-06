using System.Net.Http.Json;
using Shared.DataTransferObjects;

namespace BamboeUp.Mobile.Modules.Services;

public sealed class HolidayApi
{
    private readonly HttpClient _http;

    public HolidayApi(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(DateTime start, DateTime end, CancellationToken cancellationToken = default)
    {
        var url = $"api/holidays/calendar/events?start={Uri.EscapeDataString(start.ToString("O"))}&end={Uri.EscapeDataString(end.ToString("O"))}";
        var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CalendarEventDto>>(cancellationToken) ?? [];
    }

    public async Task<HolidayDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/holidays/{id}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<HolidayDto>(cancellationToken);
    }

    public async Task CreateAsync(HolidayForCreationDto dto, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("api/holidays", dto, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await Fail(response, cancellationToken);
    }

    public async Task UpdateAsync(Guid id, HolidayForUpdateDto dto, CancellationToken cancellationToken = default)
    {
        var response = await _http.PutAsJsonAsync($"api/holidays/{id}", dto, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await Fail(response, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, HolidayForDeleteDto dto, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"api/holidays/{id}")
        {
            Content = JsonContent.Create(dto)
        };
        var response = await _http.SendAsync(request, cancellationToken);
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
