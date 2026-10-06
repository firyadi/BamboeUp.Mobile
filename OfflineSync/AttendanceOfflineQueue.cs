using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BamboeUp.Mobile.Shell.Services;

namespace BamboeUp.Mobile.OfflineSync;

public sealed class AttendanceOfflineQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IOfflineSyncStore _store;
    private readonly MobileAuthService _auth;

    public AttendanceOfflineQueue(IOfflineSyncStore store, MobileAuthService auth)
    {
        _store = store;
        _auth = auth;
    }

    public async Task<Guid> EnqueuePunchAsync(
        bool checkIn,
        Guid attendanceGuid,
        Stream photo,
        string photoFileName,
        Location location,
        CancellationToken cancellationToken = default)
    {
        var user = _auth.CurrentUser ?? throw new InvalidOperationException("Sign in before recording attendance.");
        if (user.ActiveCompanyId is not > 0 || user.ActiveOfficeId is not > 0 || user.PersonId is not > 0)
            throw new InvalidOperationException("The current session has no company, office, or person scope.");
        if (await _store.GetCurrentScopeAsync() is null)
            throw new InvalidOperationException("The current session has no stable user/company GUID scope.");

        var staged = await _store.StageBlobAsync(photo, photoFileName, "image/jpeg", cancellationToken);
        var entityGuid = attendanceGuid == Guid.Empty ? Guid.NewGuid() : attendanceGuid;
        var operationId = Guid.NewGuid();
        var payload = new AttendancePunchEvent(
            entityGuid,
            user.ActiveCompanyId.Value,
            user.ActiveOfficeId.Value,
            user.PersonId.Value,
            user.UserId,
            checkIn ? "CheckIn" : "CheckOut",
            DateTime.UtcNow,
            (decimal)location.Latitude,
            (decimal)location.Longitude,
            location.Accuracy is null ? null : (int)Math.Round(location.Accuracy.Value),
            "MOBILE",
            staged.BlobId);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

        await _store.EnqueueAsync(new SyncOperation
        {
            OperationId = operationId.ToString("D"),
            EntityGuid = entityGuid.ToString("D"),
            EntityType = "Attendance",
            OperationType = "AttendancePunch",
            PayloadVersion = 1,
            PayloadHash = hash,
            PayloadJson = json,
            BlobId = staged.BlobId
        });
        return operationId;
    }
}
