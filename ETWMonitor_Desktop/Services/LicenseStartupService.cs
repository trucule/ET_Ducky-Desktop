using EtwMonitor.Desktop.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace EtwMonitor.Desktop.Services
{
    public class LicenseStartupService : IHostedService
    {
        private readonly LicenseValidationService _licenseService;
        private readonly ILogger<LicenseStartupService> _logger;

        public LicenseStartupService(
            LicenseValidationService licenseService,
            ILogger<LicenseStartupService> logger)
        {
            _licenseService = licenseService;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Checking license on startup...");

            var result = _licenseService.ValidateLicense();

            if (!result.IsValid && result.RequiresDomainCheck)
            {
                _logger.LogWarning("No valid license found on domain-joined device");
                // In a real app, you might show a dialog here
                // For now, we'll just log it and let the enforcement service handle it
            }
            else
            {
                _logger.LogInformation($"License status: {result.Message}");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}