using BamboeUp.Mobile.Modules;
using BamboeUp.Mobile.OfflineSync;
using BamboeUp.Mobile.Shell;
using BamboeUp.Mobile.Shell.Offline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Biometric;

namespace BamboeUp.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

        using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json")
            .GetAwaiter().GetResult();
        var config = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
        builder.Configuration.AddConfiguration(config);

        builder.Services.AddSingleton(BiometricAuthenticationService.Default);
        builder.Services.AddMobileShell(builder.Configuration);
        builder.Services.AddMobileModules();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        // Warm the session first so the lazy sync store opens only the matching user/company scope.
        _ = InitializeMobileServicesAsync(app.Services);

        return app;
    }

    private static async Task InitializeMobileServicesAsync(IServiceProvider services)
    {
        await services.GetRequiredService<Shell.Services.MobileAuthService>().InitializeAsync();
        await services.GetRequiredService<ISyncCoordinator>().StartAsync();
        await services.GetRequiredService<IPhysicalInventorySyncCoordinator>().StartAsync();
    }
}
