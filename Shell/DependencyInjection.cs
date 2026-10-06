using BamboeUp.Mobile.Shell.Device;
using BamboeUp.Mobile.Shell.Offline;
using BamboeUp.Mobile.Shell.Services;
using BamboeUp.Mobile.OfflineSync;
using Microsoft.Extensions.Configuration;
using Shared.Settings;

namespace BamboeUp.Mobile.Shell;

public static class ShellDependencyInjection
{
    public static IServiceCollection AddMobileShell(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ApiSettings>(configuration.GetSection("ApiSettings"));

        var baseUrl = configuration["ApiSettings:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "http://localhost:5000/";
        var baseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

        services.AddSingleton<MobileAuthService>();
        services.AddSingleton<IPhysicalInventoryApi, PhysicalInventoryApi>();
        services.AddSingleton<MobilePermissionService>();
        services.AddSingleton<IDeviceServices, DeviceServices>();
        services.AddSingleton<IOfflineStorageEnvironment, MauiOfflineStorageEnvironment>();
        services.AddSingleton<IPhysicalInventoryLocalStore>(sp =>
        {
            var env = sp.GetRequiredService<IOfflineStorageEnvironment>();
            var auth = sp.GetRequiredService<MobileAuthService>();
            return new PhysicalInventoryLocalStore(env, () =>
            {
                var user = auth.CurrentUser;
                if (user is null || user.UserGuid == Guid.Empty || user.ActiveCompanyGuid is not Guid company || company == Guid.Empty)
                    return Task.FromResult<SyncScope?>(null);
                return Task.FromResult<SyncScope?>(new SyncScope(user.UserGuid, company));
            });
        });
        services.AddSingleton<IPhysicalInventorySyncCoordinator, PhysicalInventorySyncCoordinator>();
        services.AddSingleton<IOfflineSyncStore, OfflineSyncStore>();
        services.AddSingleton<AttendanceOfflineQueue>();
        services.AddSingleton<ISyncCoordinator, SyncCoordinator>();

        services.AddHttpClient<IBamboeUpApiClient, BamboeUpApiClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddTransient<BearerAuthHandler>();
        services.AddHttpClient("BamboeUp.Mobile.Authenticated", client =>
            {
                client.BaseAddress = baseAddress;
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<BearerAuthHandler>();

        services.AddScoped(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return factory.CreateClient("BamboeUp.Mobile.Authenticated");
        });

        return services;
    }
}
