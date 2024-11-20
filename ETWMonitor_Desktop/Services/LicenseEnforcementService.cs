using System;
using System.Threading.Tasks;
using EtwMonitor.Desktop.Models;
using EtwMonitor.Desktop.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EtwMonitor.Desktop.Services
{
    /// <summary>
    /// Background service that checks license status on startup and periodically
    /// </summary>
    public class LicenseEnforcementService : IHostedService
    {
        private readonly LicenseValidationService _licenseService;
        private readonly DomainDetectionService _domainService;
        private readonly ILogger<LicenseEnforcementService> _logger;
        private Timer? _timer;

        public LicenseEnforcementService(
            LicenseValidationService licenseService,
            DomainDetectionService domainService,
            ILogger<LicenseEnforcementService> logger)
        {
            _licenseService = licenseService;
            _domainService = domainService;
            _logger = logger;
        }

        public event EventHandler<LicenseStatusChangedEventArgs>? LicenseStatusChanged;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("License Enforcement Service starting...");
            
            // Check immediately on startup
            CheckLicense();

            // Check every hour
            _timer = new Timer(
                callback: _ => CheckLicense(),
                state: null,
                dueTime: TimeSpan.FromHours(1),
                period: TimeSpan.FromHours(1)
            );

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("License Enforcement Service stopping...");
            _timer?.Dispose();
            return Task.CompletedTask;
        }

        private void CheckLicense()
        {
            try
            {
                // Update corporate device detection status
                _domainService.RecordCorporateDeviceDetection();

                // Validate license
                var result = _licenseService.ValidateLicense();

                _logger.LogInformation(
                    "License check: Valid={IsValid}, Type={LicenseType}, Message={Message}, GracePeriod={IsGracePeriod}",
                    result.IsValid,
                    result.License?.Type,
                    result.Message,
                    result.IsGracePeriod
                );

                // Log detection details if corporate device
                if (result.DetectionInfo?.IsCorporateDevice == true)
                {
                    _logger.LogInformation(
                        "Corporate device detected: {DetectionSummary}",
                        result.DetectionInfo.GetDetectionSummary()
                    );
                }

                // Raise event
                LicenseStatusChanged?.Invoke(this, new LicenseStatusChangedEventArgs
                {
                    ValidationResult = result
                });

                // If in grace period, log warning with days remaining
                if (result.IsGracePeriod && result.GracePeriodDaysRemaining.HasValue)
                {
                    _logger.LogWarning(
                        "Corporate device running on grace period: {DaysRemaining} days remaining. Contact smith.cm717@gmail.com for licensing.",
                        result.GracePeriodDaysRemaining.Value
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during license check");
            }
        }

        public LicenseValidationResult GetCurrentStatus()
        {
            return _licenseService.ValidateLicense();
        }
    }

    public class LicenseStatusChangedEventArgs : EventArgs
    {
        public LicenseValidationResult ValidationResult { get; set; } = null!;
    }
}
