using System.Net.Http.Json;
using Shared.DataTransferObjects;

namespace BamboeUp.Mobile.Modules.Services;

public sealed class PayrollRunApi
{
    private readonly HttpClient _http;

    public PayrollRunApi(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<ProcessDefinitionDto>> GetDefinitionsAsync(string processCode, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/processes?processCode={Uri.EscapeDataString(processCode)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ProcessDefinitionDto>>(cancellationToken) ?? [];
    }

    public async Task<ProcessDefinitionDto?> GetDefinitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/processes/{id}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<ProcessDefinitionDto>(cancellationToken);
    }

    public async Task<ProcessValidationResultDto> ValidateAsync(Guid definitionId, ProcessValidateRequestDto input, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync($"api/processes/{definitionId}/validate", input, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await Fail(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ProcessValidationResultDto>(cancellationToken) ?? new ProcessValidationResultDto();
    }

    public async Task<ProcessExecutionDto> ExecuteAsync(Guid definitionId, ProcessExecuteRequestDto input, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync($"api/processes/{definitionId}/execute", input, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await Fail(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ProcessExecutionDto>(cancellationToken)
               ?? throw new HttpRequestException("The process did not return an execution.");
    }

    public async Task<ProcessExecutionDto?> GetExecutionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/processes/executions/{id}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<ProcessExecutionDto>(cancellationToken);
    }

    public async Task<IReadOnlyList<ProcessMessageDto>> GetMessagesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/processes/executions/{id}/messages", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ProcessMessageDto>>(cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<ProcessResultDetailDto>> GetResultDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/processes/executions/{id}/result-details", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ProcessResultDetailDto>>(cancellationToken) ?? [];
    }

    public async Task CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"api/processes/executions/{id}/cancel", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await Fail(response, cancellationToken);
    }

    private static async Task<HttpRequestException> Fail(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new HttpRequestException(string.IsNullOrWhiteSpace(body)
            ? $"Process failed ({(int)response.StatusCode})."
            : body);
    }
}
