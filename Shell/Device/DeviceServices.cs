namespace BamboeUp.Mobile.Shell.Device;

public interface IDeviceServices
{
    Task<bool> EnsureCameraPermissionAsync();
    Task<FileResult?> CapturePhotoAsync();
    Task<string?> ScanBarcodeAsync();
    Task<Guid> GetDeviceGuidAsync();
    Task<Location?> GetCurrentLocationAsync();
    /// <summary>Fingerprint / Face ID via Plugin.Maui.Biometric when available.</summary>
    Task<bool> AuthenticateBiometricAsync(string reason);
}

/// <summary>MAUI Essentials + optional biometric plugin wrappers.</summary>
public sealed class DeviceServices : IDeviceServices
{
    private const string DeviceGuidKey = "bamboeup_device_guid";

    public async Task<bool> EnsureCameraPermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.Camera>();
        return status == PermissionStatus.Granted;
    }

    public Task<FileResult?> CapturePhotoAsync() =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (!MediaPicker.Default.IsCaptureSupported)
                return null;
            if (!await EnsureCameraPermissionAsync())
                return null;
            return await MediaPicker.Default.CapturePhotoAsync();
        });

    public async Task<string?> ScanBarcodeAsync()
    {
        var photo = await CapturePhotoAsync();
        if (photo is null)
            return null;
        await using var stream = await photo.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return BarcodePhotoDecoder.Decode(memory.ToArray());
    }

    public async Task<Guid> GetDeviceGuidAsync()
    {
        try
        {
            var stored = await SecureStorage.Default.GetAsync(DeviceGuidKey);
            if (Guid.TryParse(stored, out var existing) && existing != Guid.Empty)
                return existing;
            var created = Guid.NewGuid();
            await SecureStorage.Default.SetAsync(DeviceGuidKey, created.ToString("D"));
            return created;
        }
        catch
        {
            return Guid.Parse("00000000-0000-0000-0000-000000000001");
        }
    }

    public Task<Location?> GetCurrentLocationAsync() =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                return null;

            return await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)));
        });

    public async Task<bool> AuthenticateBiometricAsync(string reason)
    {
        try
        {
            var biometric = Plugin.Maui.Biometric.BiometricAuthenticationService.Default;
            var result = await biometric.AuthenticateAsync(
                new Plugin.Maui.Biometric.AuthenticationRequest
                {
                    Title = "BamboeUp.Mobile",
                    Subtitle = reason,
                    NegativeText = "Cancel"
                },
                CancellationToken.None);
            return result.Status == Plugin.Maui.Biometric.BiometricResponseStatus.Success;
        }
        catch
        {
            return false;
        }
    }
}
