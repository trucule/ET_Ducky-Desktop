using System;
using EtwMonitor.Desktop.Services;

namespace EtwMonitor.Desktop.Models
{
    public enum LicenseType
    {
        Free,           // Non-domain devices
        Trial,          // 7-day trial
        Monthly,        // Monthly subscription
        Lifetime        // Lifetime license
    }

    public enum LicenseStatus
    {
        Valid,
        Expired,
        Invalid,
        Revoked
    }

    public class License
    {
        public string LicenseKey { get; set; } = string.Empty;
        public LicenseType Type { get; set; }
        public DateTime IssuedDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string MachineId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsRevoked { get; set; }
        public string Signature { get; set; } = string.Empty;

        public LicenseStatus GetStatus()
        {
            if (IsRevoked)
                return LicenseStatus.Revoked;

            if (Type == LicenseType.Free)
                return LicenseStatus.Valid;

            if (Type == LicenseType.Lifetime)
                return LicenseStatus.Valid;

            if (DateTime.UtcNow > ExpiryDate)
                return LicenseStatus.Expired;

            return LicenseStatus.Valid;
        }

        public int DaysRemaining()
        {
            if (Type == LicenseType.Lifetime)
                return int.MaxValue;

            if (Type == LicenseType.Free)
                return int.MaxValue;

            var remaining = (ExpiryDate - DateTime.UtcNow).Days;
            return Math.Max(0, remaining);
        }
    }

    public class LicenseValidationResult
    {
        public bool IsValid { get; set; }
        public LicenseStatus Status { get; set; }
        public License? License { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool RequiresDomainCheck { get; set; }
        
        // New properties for grace period support
        public bool IsGracePeriod { get; set; }
        public int? GracePeriodDaysRemaining { get; set; }
        public CorporateDeviceDetectionInfo? DetectionInfo { get; set; }
    }
}
