using BamboeUp.Mobile.Shell.Offline;

namespace BamboeUp.Mobile.Shell.Offline;

public interface IOfflineStorageEnvironment
{
    string AppDataDirectory { get; }
    Task<string?> GetSecureAsync(string key);
    Task SetSecureAsync(string key, string value);
}

public sealed class MauiOfflineStorageEnvironment : IOfflineStorageEnvironment
{
    public string AppDataDirectory => FileSystem.AppDataDirectory;

    public async Task<string?> GetSecureAsync(string key)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetSecureAsync(string key, string value)
    {
        try
        {
            await SecureStorage.Default.SetAsync(key, value);
        }
        catch
        {
            // Some hosts (Windows unpackaged) may reject SecureStorage; keep going with in-memory fallback.
        }
    }
}
