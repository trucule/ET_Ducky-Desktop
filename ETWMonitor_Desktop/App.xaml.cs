using EtwMonitor.Desktop.Services;
using EtwMonitor.Desktop.Models;

namespace EtwMonitor.Desktop;

public partial class App : Application
{
    private readonly LicenseValidationService? _licenseService;

    public App()
    {
        InitializeComponent();
    }

    // Constructor with dependency injection (when services are available)
    public App(LicenseValidationService licenseService)
    {
        InitializeComponent();
        _licenseService = licenseService;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage())
        {
            Title = "ET Ducky",
            MinimumWidth = 1200,
            MinimumHeight = 800
        };

        // Check license on startup
        if (_licenseService != null)
        {
            window.Created += async (s, e) =>
            {
                await CheckLicenseOnStartup();
            };
        }

        return window;
    }

    private async Task CheckLicenseOnStartup()
    {
        if (_licenseService == null)
            return;

        try
        {
            var result = _licenseService.ValidateLicense();

            // Log the license status
            System.Diagnostics.Debug.WriteLine($"License Status: {result.Message}");

            // If license is invalid and required (domain-joined device)
            if (!result.IsValid && result.RequiresDomainCheck)
            {
                // Show alert on main thread
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    bool activateNow = await Application.Current.MainPage.DisplayAlert(
                        "License Required",
                        "This device requires a license to use ET Ducky.\n\n" +
                        "Would you like to activate a license now?",
                        "Activate License",
                        "Continue Anyway"
                    );

                    if (activateNow)
                    {
                        // Navigate to license page
                        // Note: You'll need to adjust this based on your navigation setup
                        // If using Shell: await Shell.Current.GoToAsync("//license");
                        // If using NavigationPage: await Application.Current.MainPage.Navigation.PushAsync(new LicensePage());
                        
                        // For now, just show a message
                        await Application.Current.MainPage.DisplayAlert(
                            "License Activation",
                            "Please navigate to the License page from the menu to activate your license.",
                            "OK"
                        );
                    }
                });
            }
            else if (result.IsValid && result.License != null)
            {
                // License is valid - log the details
                if (result.License.Type != LicenseType.Free)
                {
                    System.Diagnostics.Debug.WriteLine($"Licensed to: {result.License.Email}");
                    System.Diagnostics.Debug.WriteLine($"License type: {result.License.Type}");
                    
                    // Show remaining days for trial/monthly licenses
                    if (result.License.Type == LicenseType.Trial || result.License.Type == LicenseType.Monthly)
                    {
                        var daysRemaining = result.License.DaysRemaining();
                        System.Diagnostics.Debug.WriteLine($"Days remaining: {daysRemaining}");
                        
                        // Warn if less than 7 days remaining
                        if (daysRemaining <= 7 && daysRemaining > 0)
                        {
                            await MainThread.InvokeOnMainThreadAsync(async () =>
                            {
                                await Application.Current.MainPage.DisplayAlert(
                                    "License Expiring Soon",
                                    $"Your {result.License.Type} license will expire in {daysRemaining} day(s).\n\n" +
                                    "Please renew your license to continue using ET Ducky.",
                                    "OK"
                                );
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking license on startup: {ex.Message}");
        }
    }
}
