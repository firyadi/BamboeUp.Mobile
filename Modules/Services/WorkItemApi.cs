using System.Net.Http.Json;
using Shared.DataTransferObjects;

namespace BamboeUp.Mobile.Modules.Services;

public sealed class WorkItemApi
{
    private readonly HttpClient _http;

    public WorkItemApi(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<WorkItemDto>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("api/workItems", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<WorkItemDto>>(cancellationToken) ?? [];
    }

    public async Task<WorkItemDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/workItems/{id}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<WorkItemDto>(cancellationToken);
    }

    public async Task CreateAsync(WorkItemForCreationDto dto, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("api/workItems", dto, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await Fail(response, cancellationToken);
    }

    public async Task UpdateAsync(Guid id, WorkItemForUpdateDto dto, CancellationToken cancellationToken = default)
    {
        var response = await _http.PutAsJsonAsync($"api/workItems/{id}", dto, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await Fail(response, cancellationToken);
    }

    public async Task<KanbanBoardDto> GetBoardAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.GetAsync("api/workItems/kanban/board", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var board = await response.Content.ReadFromJsonAsync<KanbanBoardDto>(cancellationToken);
                if (board is not null)
                    return board;
            }
        }
        catch (HttpRequestException)
        {
        }

        var list = await GetListAsync(cancellationToken);
        return new KanbanBoardDto
        {
            BoardTitle = "Work Item",
            Cards = list.Select(Map).ToList()
        };
    }

    public async Task MoveAsync(KanbanCardMoveDto move, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/workItems/kanban/move", move, cancellationToken);
            if (response.IsSuccessStatusCode)
                return;
        }
        catch (HttpRequestException)
        {
        }

        var item = await GetByIdAsync(move.CardGuid, cancellationToken)
                   ?? throw new InvalidOperationException("Work item was not found.");
        await UpdateAsync(move.CardGuid, new WorkItemForUpdateDto
        {
            WorkItemGuid = item.WorkItemGuid,
            OwnerUserId = item.OwnerUserId,
            WorkItemCode = item.WorkItemCode,
            Title = item.Title,
            Description = item.Description,
            Module = item.Module,
            WorkItemType = item.WorkItemType,
            OwnerName = item.OwnerName,
            WorkflowStatus = move.ToStatusCode,
            Priority = item.Priority,
            CompletionPercent = item.CompletionPercent,
            Tags = item.Tags,
            Framework = item.Framework,
            DueDate = item.DueDate,
            Note = item.Note,
            UpdatedById = item.UpdatedById ?? item.CreatedById
        }, cancellationToken);
    }

    private static KanbanCardDto Map(WorkItemDto item)
    {
        var status = string.IsNullOrWhiteSpace(item.WorkflowStatus) ? "DRAFT" : item.WorkflowStatus.Trim().ToUpperInvariant();
        return new KanbanCardDto
        {
            CardGuid = item.WorkItemGuid,
            Title = item.Title,
            Code = item.WorkItemCode,
            StatusCode = status,
            StatusName = status.Replace('_', ' '),
            Type = item.WorkItemType,
            Module = item.Module,
            Owner = item.OwnerName ?? item.OwnerUserName,
            Description = item.Description,
            Priority = item.Priority,
            DueDate = item.DueDate,
            CompletionPercent = item.CompletionPercent,
            Tags = item.Tags
        };
    }

    private static async Task<HttpRequestException> Fail(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new HttpRequestException(string.IsNullOrWhiteSpace(body)
            ? $"Save failed ({(int)response.StatusCode})."
            : body);
    }
}
