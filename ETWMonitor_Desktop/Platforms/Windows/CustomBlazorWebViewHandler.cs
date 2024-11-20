#if WINDOWS
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml.Controls;

namespace EtwMonitor.Desktop.Platforms.Windows;

public class CustomBlazorWebViewHandler : BlazorWebViewHandler
{
    private static readonly string UserDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ETDucky",
        "WebView2"
    );

    protected override WebView2 CreatePlatformView()
    {
        // Ensure the user data directory exists
        Directory.CreateDirectory(UserDataFolder);

        // Create the WebView2 control
        var webView = new WebView2();

        // Initialize with custom environment
        _ = InitializeWebView2Async(webView);

        return webView;
    }

    private async Task InitializeWebView2Async(WebView2 webView)
    {
        try
        {
            // Create environment options
            var options = new CoreWebView2EnvironmentOptions();

            // Create environment with custom user data folder using CreateWithOptionsAsync
            var env = await CoreWebView2Environment.CreateWithOptionsAsync(
                browserExecutableFolder: null,
                userDataFolder: UserDataFolder,
                options: options);

            // Initialize the WebView2 with this environment
            await webView.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebView2 initialization error: {ex.Message}");
        }
    }
}
#endif