using EtwMonitor.Desktop.Services;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using System.IO;

#if WINDOWS
using EtwMonitor.Desktop.Platforms.Windows;
#endif

namespace EtwMonitor.Desktop;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
#if WINDOWS
        // Ensure WebView2 stores its data in a user-writable folder
        var webViewDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ETWMonitor",
            "WebView2");

        Directory.CreateDirectory(webViewDataFolder);

        // Tell WebView2 to use this directory for its cache / data
        Environment.SetEnvironmentVariable(
            "WEBVIEW2_USER_DATA_FOLDER",
            webViewDataFolder,
            EnvironmentVariableTarget.Process);
#endif

        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // MudBlazor
        builder.Services.AddMudServices();

        // App services
        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<MonitorStateService>();
        builder.Services.AddSingleton<DiagnosticsService>();
        builder.Services.AddSingleton<AIProviderService>();
        builder.Services.AddSingleton<AIAnalysisService>();

        // License services - MUST be in this order with proper factory
        builder.Services.AddSingleton<DomainDetectionService>();
        builder.Services.AddSingleton<LicenseValidationService>(sp =>
        {
            var domainService = sp.GetRequiredService<DomainDetectionService>();
            return new LicenseValidationService(
                LicenseConfiguration.PublicKey,
                domainService
            );
        });
        builder.Services.AddHostedService<LicenseEnforcementService>();
        builder.Services.AddHostedService<LicenseStartupService>();

        return builder.Build();
    }
}