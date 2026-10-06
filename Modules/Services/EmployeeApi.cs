using System.Net.Http.Json;
using Shared.DataTransferObjects;

namespace BamboeUp.Mobile.Modules.Services;

public sealed class EmployeeApi
{
    private readonly HttpClient _http;

    public EmployeeApi(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<EmployeeDto>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("api/employees", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<EmployeeDto>>(cancellationToken) ?? [];
    }

    public async Task<EmployeeDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/employees/{id}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<EmployeeDto>(cancellationToken);
    }

    public Task CreateAsync(EmployeeForCreationDto dto, CancellationToken cancellationToken = default) =>
        SendAsync(() => _http.PostAsJsonAsync("api/employees", dto, cancellationToken), cancellationToken);

    public Task UpdateAsync(Guid id, EmployeeForUpdateDto dto, CancellationToken cancellationToken = default) =>
        SendAsync(() => _http.PutAsJsonAsync($"api/employees/{id}", dto, cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<EmployeeAssignmentDto>> GetAssignmentsAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/employees/{employeeId}/employeeAssignments", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<EmployeeAssignmentDto>>(cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<EmployeeContractDto>> GetContractsAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/employees/{employeeId}/employeeContracts", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<EmployeeContractDto>>(cancellationToken) ?? [];
    }

    private static async Task SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        var response = await send();
        if (response.IsSuccessStatusCode)
            return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(string.IsNullOrWhiteSpace(body)
            ? $"Save failed ({(int)response.StatusCode})."
            : body);
    }
}
