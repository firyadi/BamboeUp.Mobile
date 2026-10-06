using Shared.DataTransferObjects;
using SQLite;

namespace BamboeUp.Mobile.Shell.Offline;

[Table("local_inventory_counts")]
public sealed class LocalInventoryCount
{
    [PrimaryKey] public string CaptureGuid { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public string CounterUserGuid { get; set; } = string.Empty;
    public string DeviceGuid { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public string Status { get; set; } = PhysicalInventoryStatuses.InProgress;
    public string? LastError { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

[Table("local_inventory_count_lines")]
public sealed class LocalInventoryCountLine
{
    [PrimaryKey] public string LineGuid { get; set; } = string.Empty;
    [Indexed] public string CaptureGuid { get; set; } = string.Empty;
    [Indexed] public string Barcode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int ScanCount { get; set; }
    public DateTime FirstCapturedUtc { get; set; }
    public DateTime LastCapturedUtc { get; set; }
    public string Source { get; set; } = "Manual";
}

[Table("local_sync_receipts")]
public sealed class LocalSyncReceipt
{
    [PrimaryKey] public string OperationId { get; set; } = string.Empty;
    public string CaptureGuid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PayloadHash { get; set; }
    public string? ServerMessage { get; set; }
    public long? ConcurrencyToken { get; set; }
    public DateTime CommittedUtc { get; set; }
}

public sealed record PhysicalInventorySessionSummary(
    string CaptureGuid,
    string DocumentNumber,
    int PageNumber,
    string Status,
    int LineCount,
    decimal TotalQuantity,
    DateTime UpdatedUtc,
    string? LastError);

public sealed record PhysicalInventorySyncSnapshot(
    int InProgress,
    int PendingUpload,
    int Uploading,
    int Uploaded,
    int Conflict,
    int Rejected)
{
    public int Waiting => PendingUpload + Uploading;
    public int RequiresAttention => Conflict + Rejected;
}
