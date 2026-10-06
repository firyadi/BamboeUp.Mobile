using System.Net.Http.Headers;
using System.Net.Http.Json;
using Shared.DataTransferObjects;

namespace BamboeUp.Mobile.Modules.Services;

public sealed class AttendanceApi
{
    private readonly HttpClient _http;

    public AttendanceApi(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<AttendanceDto>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("api/attendances", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<AttendanceDto>>(cancellationToken) ?? [];
    }

    public async Task<AttendanceDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/attendances/{id}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<AttendanceDto>(cancellationToken);
    }

    public async Task CreateAsync(AttendanceForCreationDto dto, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("api/attendances", dto, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(string.IsNullOrWhiteSpace(body) ? $"Save failed ({(int)response.StatusCode})." : body);
        }
    }

    public async Task UpdateAsync(Guid id, AttendanceForUpdateDto dto, CancellationToken cancellationToken = default)
    {
        var response = await _http.PutAsJsonAsync($"api/attendances/{id}", dto, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(string.IsNullOrWhiteSpace(body) ? $"Save failed ({(int)response.StatusCode})." : body);
        }
    }

    public async Task<long> UploadPhotoAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var stream = new StreamContent(content);
        stream.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(stream, "file", fileName);
        var response = await _http.PostAsync("api/files", form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(string.IsNullOrWhiteSpace(body) ? $"Upload failed ({(int)response.StatusCode})." : body);
        var wrapper = System.Text.Json.JsonSerializer.Deserialize<FileOperationResult<FileStorageDto>>(body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (wrapper?.Data is null || wrapper.Data.FileStorageId <= 0)
            throw new HttpRequestException(wrapper?.Message ?? "Upload failed.");
        return wrapper.Data.FileStorageId;
    }
}
