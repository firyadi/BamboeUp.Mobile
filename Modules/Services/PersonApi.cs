using System.Net.Http.Json;
using Shared.DataTransferObjects;

namespace BamboeUp.Mobile.Modules.Services;

public sealed class PersonApi
{
    private readonly HttpClient _http;

    public PersonApi(HttpClient http) => _http = http;

    public Task<IReadOnlyList<PersonDto>> GetListAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<PersonDto>("api/people", cancellationToken);

    public Task<PersonDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<PersonDto>($"api/people/{id}", cancellationToken);

    public Task CreateAsync(PersonForCreationDto dto, CancellationToken cancellationToken = default) =>
        PostAsync("api/people", dto, cancellationToken);

    public Task UpdateAsync(Guid id, PersonForUpdateDto dto, CancellationToken cancellationToken = default) =>
        PutAsync($"api/people/{id}", dto, cancellationToken);

    public Task OnboardAsync(PersonQuickOnboardDto dto, CancellationToken cancellationToken = default) =>
        PostAsync("api/people/onboard", dto, cancellationToken);

    public Task<IReadOnlyList<PersonAddressDto>> GetAddressesAsync(Guid personId, CancellationToken cancellationToken = default) =>
        GetListAsync<PersonAddressDto>($"api/people/{personId}/personAddresses", cancellationToken);

    public Task AddAddressAsync(Guid personId, PersonAddressForCreationDto dto, CancellationToken cancellationToken = default) =>
        PostAsync($"api/people/{personId}/personAddresses", dto, cancellationToken);

    public Task<IReadOnlyList<PersonContactDto>> GetContactsAsync(Guid personId, CancellationToken cancellationToken = default) =>
        GetListAsync<PersonContactDto>($"api/people/{personId}/personContacts", cancellationToken);

    public Task AddContactAsync(Guid personId, PersonContactForCreationDto dto, CancellationToken cancellationToken = default) =>
        PostAsync($"api/people/{personId}/personContacts", dto, cancellationToken);

    public Task<IReadOnlyList<PersonEducationDto>> GetEducationsAsync(Guid personId, CancellationToken cancellationToken = default) =>
        GetListAsync<PersonEducationDto>($"api/people/{personId}/personEducations", cancellationToken);

    public Task AddEducationAsync(Guid personId, PersonEducationForCreationDto dto, CancellationToken cancellationToken = default) =>
        PostAsync($"api/people/{personId}/personEducations", dto, cancellationToken);

    public Task<IReadOnlyList<PersonEmergencyContactDto>> GetEmergencyContactsAsync(Guid personId, CancellationToken cancellationToken = default) =>
        GetListAsync<PersonEmergencyContactDto>($"api/people/{personId}/personEmergencyContacts", cancellationToken);

    public Task AddEmergencyContactAsync(Guid personId, PersonEmergencyContactForCreationDto dto, CancellationToken cancellationToken = default) =>
        PostAsync($"api/people/{personId}/personEmergencyContacts", dto, cancellationToken);

    public Task<IReadOnlyList<PersonIdentificationDto>> GetIdentificationsAsync(Guid personId, CancellationToken cancellationToken = default) =>
        GetListAsync<PersonIdentificationDto>($"api/people/{personId}/personIdentifications", cancellationToken);

    public Task AddIdentificationAsync(Guid personId, PersonIdentificationForCreationDto dto, CancellationToken cancellationToken = default) =>
        PostAsync($"api/people/{personId}/personIdentifications", dto, cancellationToken);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string url, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken) ?? [];
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return default;
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private async Task PostAsync<T>(string url, T dto, CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync(url, dto, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await Fail(response, cancellationToken);
    }

    private async Task PutAsync<T>(string url, T dto, CancellationToken cancellationToken)
    {
        var response = await _http.PutAsJsonAsync(url, dto, cancellationToken);
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
