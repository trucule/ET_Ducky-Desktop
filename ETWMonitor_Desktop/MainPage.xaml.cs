using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace EtwMonitor.Desktop;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

        // Add error handler
        var blazorWebView = this.FindByName<BlazorWebView>("blazorWebView");
        if (blazorWebView != null)
        {
            blazorWebView.BlazorWebViewInitialized += (s, e) =>
            {
                e.WebView.CoreWebView2.NavigationCompleted += async (sender, args) =>
                {
                    if (!args.IsSuccess)
                    {
                        await DisplayAlert("Error", $"Navigation failed: {args.WebErrorStatus}", "OK");
                    }
                };
            };
        }
    }
}