using System.Text.Json.Serialization;
using SQLite;

namespace BamboeUp.Mobile.OfflineSync;

public enum SyncOperationStatus
{
    Pending,
    Uploading,
    Applied,
    Retryable,
    Conflict,
    Rejected
}

[Table("sync_operations")]
public sealed class SyncOperation
{
    [PrimaryKey] public string OperationId { get; set; } = string.Empty;
    [Indexed] public string EntityGuid { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public int PayloadVersion { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string? BlobId { get; set; }
    [Indexed] public int StatusValue { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public string? LastError { get; set; }

    [Ignore, JsonIgnore]
    public SyncOperationStatus Status
    {
        get => (SyncOperationStatus)StatusValue;
        set => StatusValue = (int)value;
    }
}

[Table("sync_receipts")]
public sealed class SyncReceipt
{
    [PrimaryKey] public string OperationId { get; set; } = string.Empty;
    public int StatusValue { get; set; }
    public DateTime ReceivedUtc { get; set; }
    public string? ServerMessage { get; set; }
    public string? ServerVersion { get; set; }

    [Ignore, JsonIgnore]
    public SyncOperationStatus Status
    {
        get => (SyncOperationStatus)StatusValue;
        set => StatusValue = (int)value;
    }
}

[Table("staged_blobs")]
public sealed class SyncBlob
{
    [PrimaryKey] public string BlobId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Length { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed record SyncScope(Guid UserGuid, Guid CompanyGuid)
{
    public string StableId => $"{UserGuid:N}_{CompanyGuid:N}";
}

public sealed record SyncQueueSummary(int Pending, int Uploading, int Retryable, int Conflict, int Rejected)
{
    public int RequiresAttention => Conflict + Rejected;
    public int Waiting => Pending + Uploading + Retryable;
}

public sealed record StagedBlob(string BlobId, string FileName, string ContentType, long Length);

public sealed record SyncOperationEnvelope(
    Guid OperationId,
    Guid EntityGuid,
    string EntityType,
    string OperationType,
    int PayloadVersion,
    string PayloadHash,
    string PayloadJson);

public sealed record SyncServerReceipt(
    Guid OperationId,
    SyncOperationStatus Status,
    string? Message,
    string? ServerVersion);

public sealed record AttendancePunchEvent(
    Guid AttendanceGuid,
    long CompanyId,
    long CompanyOfficeId,
    long PersonId,
    long UserId,
    string Kind,
    DateTime OccurredUtc,
    decimal Latitude,
    decimal Longitude,
    int? AccuracyMeters,
    string Channel,
    string PhotoBlobId);
