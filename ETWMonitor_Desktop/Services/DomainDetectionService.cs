using System;
using System.DirectoryServices.ActiveDirectory;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using Microsoft.Win32;

namespace EtwMonitor.Desktop.Services
{
    public class DomainDetectionService
    {
        private const string LastDomainCheckFile = "last_domain_check.dat";
        private const string GracePeriodFile = "grace_period.dat";
        private readonly string _dataPath;

        public DomainDetectionService()
        {
            _dataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ETDucky"
            );
            Directory.CreateDirectory(_dataPath);
        }

        /// <summary>
        /// Checks if the device is a corporate/enterprise device using multiple detection methods
        /// </summary>
        public bool IsCorporateDevice()
        {
            return IsTraditionalDomainJoined() || 
                   IsAzureADJoined() || 
                   IsIntuneEnrolled() ||
                   IsWorkplaceJoined() ||
                   HasMDMEnrollment();
        }

        /// <summary>
        /// Checks if the device is traditionally domain-joined (Active Directory)
        /// </summary>
        public bool IsTraditionalDomainJoined()
        {
            try
            {
                // Method 1: Check if the computer is part of a domain
                if (Environment.UserDomainName != Environment.MachineName)
                {
                    return true;
                }

                // Method 2: Check using Win32 API
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var partOfDomain = mo["PartOfDomain"];
                        if (partOfDomain != null && (bool)partOfDomain)
                        {
                            return true;
                        }
                    }
                }

                // Method 3: Try to get domain (will throw if not domain-joined)
                try
                {
                    var domain = Domain.GetComputerDomain();
                    return domain != null;
                }
                catch
                {
                    // Not domain-joined
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the device is Azure AD (Entra ID) joined
        /// </summary>
        public bool IsAzureADJoined()
        {
            try
            {
                // Check CloudDomainJoin registry key
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo");
                
                if (key != null)
                {
                    var subKeyNames = key.GetSubKeyNames();
                    return subKeyNames.Length > 0;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the device is workplace joined (Azure AD registered)
        /// </summary>
        public bool IsWorkplaceJoined()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\WorkplaceJoin");
                
                if (key != null)
                {
                    var joinInfo = key.GetValue("WorkplaceJoin");
                    return joinInfo != null;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the device is enrolled in Microsoft Intune
        /// </summary>
        public bool IsIntuneEnrolled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Enrollments");
                
                if (key == null) return false;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    var providerId = subKey.GetValue("ProviderID")?.ToString();
                    
                    // Check for MS DM Server (Intune) or other MDM providers
                    if (!string.IsNullOrEmpty(providerId) && 
                        (providerId.Contains("MS DM Server", StringComparison.OrdinalIgnoreCase) ||
                         providerId.Contains("Intune", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks for any MDM enrollment (Mobile Device Management)
        /// </summary>
        public bool HasMDMEnrollment()
        {
            try
            {
                // Check for MDM enrollment via OMADM registry
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Provisioning\OMADM\Accounts");
                
                if (key != null)
                {
                    var subKeyNames = key.GetSubKeyNames();
                    return subKeyNames.Length > 0;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Records that a corporate device was detected
        /// </summary>
        public void RecordCorporateDeviceDetection()
        {
            if (IsCorporateDevice())
            {
                var filePath = Path.Combine(_dataPath, LastDomainCheckFile);
                File.WriteAllText(filePath, DateTime.UtcNow.ToString("O"));
            }
        }

        /// <summary>
        /// Gets the corporate device detection information
        /// </summary>
        public CorporateDeviceDetectionInfo GetDetectionInfo()
        {
            var info = new CorporateDeviceDetectionInfo
            {
                IsTraditionalDomain = IsTraditionalDomainJoined(),
                IsAzureAD = IsAzureADJoined(),
                IsIntune = IsIntuneEnrolled(),
                IsWorkplaceJoined = IsWorkplaceJoined(),
                HasMDM = HasMDMEnrollment()
            };

            info.IsCorporateDevice = info.IsTraditionalDomain || info.IsAzureAD || 
                                     info.IsIntune || info.IsWorkplaceJoined || info.HasMDM;

            if (info.IsTraditionalDomain)
            {
                info.DomainName = Environment.UserDomainName;
            }

            return info;
        }

        /// <summary>
        /// Starts a 30-day grace period for corporate device detection
        /// </summary>
        public void StartGracePeriod()
        {
            var filePath = Path.Combine(_dataPath, GracePeriodFile);
            var gracePeriod = new GracePeriodInfo
            {
                StartDate = DateTime.UtcNow,
                ExpiryDate = DateTime.UtcNow.AddDays(30),
                DetectionInfo = GetDetectionInfo()
            };

            var json = System.Text.Json.JsonSerializer.Serialize(gracePeriod);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Gets the current grace period information
        /// </summary>
        public GracePeriodInfo? GetGracePeriod()
        {
            try
            {
                var filePath = Path.Combine(_dataPath, GracePeriodFile);
                if (!File.Exists(filePath))
                    return null;

                var json = File.ReadAllText(filePath);
                return System.Text.Json.JsonSerializer.Deserialize<GracePeriodInfo>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Checks if the device is within an active grace period
        /// </summary>
        public bool IsInGracePeriod()
        {
            var gracePeriod = GetGracePeriod();
            if (gracePeriod == null)
                return false;

            return DateTime.UtcNow < gracePeriod.ExpiryDate;
        }

        /// <summary>
        /// Gets the number of days remaining in the grace period
        /// </summary>
        public int? GetGracePeriodDaysRemaining()
        {
            var gracePeriod = GetGracePeriod();
            if (gracePeriod == null || !IsInGracePeriod())
                return null;

            return (int)(gracePeriod.ExpiryDate - DateTime.UtcNow).TotalDays;
        }

        /// <summary>
        /// Clears the grace period (used when a valid license is activated)
        /// </summary>
        public void ClearGracePeriod()
        {
            var filePath = Path.Combine(_dataPath, GracePeriodFile);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        /// <summary>
        /// Records that the device is currently domain-joined (legacy method)
        /// </summary>
        [Obsolete("Use RecordCorporateDeviceDetection instead")]
        public void RecordDomainStatus()
        {
            RecordCorporateDeviceDetection();
        }

        /// <summary>
        /// Gets the last time the device was detected as domain-joined (legacy method)
        /// </summary>
        [Obsolete("Use GetDetectionInfo instead")]
        public DateTime? GetLastDomainJoinDate()
        {
            try
            {
                var filePath = Path.Combine(_dataPath, LastDomainCheckFile);
                if (!File.Exists(filePath))
                    return null;

                var dateString = File.ReadAllText(filePath);
                if (DateTime.TryParse(dateString, out var date))
                {
                    return date;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Determines if the device qualifies for free usage (personal use)
        /// </summary>
        public bool QualifiesForFreeUsage()
        {
            // If it's a corporate device, doesn't qualify for free
            if (IsCorporateDevice())
            {
                RecordCorporateDeviceDetection();
                return false;
            }

            // Personal device qualifies for free
            return true;
        }

        /// <summary>
        /// Gets the number of days since the device was last domain-joined (legacy method)
        /// </summary>
        [Obsolete("No longer used with honor system approach")]
        public int? GetDaysSinceLastDomainJoin()
        {
            var lastDate = GetLastDomainJoinDate();
            if (!lastDate.HasValue)
                return null;

            return (int)(DateTime.UtcNow - lastDate.Value).TotalDays;
        }

        /// <summary>
        /// Gets information about the current domain status
        /// </summary>
        public DomainStatusInfo GetDomainStatus()
        {
            var detectionInfo = GetDetectionInfo();
            var gracePeriod = GetGracePeriod();
            var inGracePeriod = IsInGracePeriod();
            var daysRemaining = GetGracePeriodDaysRemaining();

            return new DomainStatusInfo
            {
                IsCurrentlyDomainJoined = detectionInfo.IsCorporateDevice,
                IsCorporateDevice = detectionInfo.IsCorporateDevice,
                DetectionInfo = detectionInfo,
                QualifiesForFree = QualifiesForFreeUsage(),
                DomainName = detectionInfo.DomainName,
                IsInGracePeriod = inGracePeriod,
                GracePeriodDaysRemaining = daysRemaining,
                GracePeriodStart = gracePeriod?.StartDate,
                GracePeriodExpiry = gracePeriod?.ExpiryDate
            };
        }
    }

    public class CorporateDeviceDetectionInfo
    {
        public bool IsCorporateDevice { get; set; }
        public bool IsTraditionalDomain { get; set; }
        public bool IsAzureAD { get; set; }
        public bool IsIntune { get; set; }
        public bool IsWorkplaceJoined { get; set; }
        public bool HasMDM { get; set; }
        public string? DomainName { get; set; }

        public string GetDetectionSummary()
        {
            if (!IsCorporateDevice)
                return "Personal device detected";

            var methods = new System.Collections.Generic.List<string>();
            if (IsTraditionalDomain) methods.Add("Active Directory Domain");
            if (IsAzureAD) methods.Add("Azure AD/Entra ID");
            if (IsIntune) methods.Add("Microsoft Intune");
            if (IsWorkplaceJoined) methods.Add("Workplace Joined");
            if (HasMDM) methods.Add("MDM Enrolled");

            return $"Corporate device detected via: {string.Join(", ", methods)}";
        }
    }

    public class GracePeriodInfo
    {
        public DateTime StartDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public CorporateDeviceDetectionInfo? DetectionInfo { get; set; }
    }

    public class DomainStatusInfo
    {
        public bool IsCurrentlyDomainJoined { get; set; }
        public bool IsCorporateDevice { get; set; }
        public CorporateDeviceDetectionInfo? DetectionInfo { get; set; }
        public DateTime? LastDomainJoinDate { get; set; }
        public int? DaysSinceLastDomainJoin { get; set; }
        public bool QualifiesForFree { get; set; }
        public string? DomainName { get; set; }
        public bool IsInGracePeriod { get; set; }
        public int? GracePeriodDaysRemaining { get; set; }
        public DateTime? GracePeriodStart { get; set; }
        public DateTime? GracePeriodExpiry { get; set; }

        public string GetStatusMessage()
        {
            if (IsInGracePeriod)
            {
                return $"Corporate device detected - Grace period active ({GracePeriodDaysRemaining} days remaining)";
            }

            if (IsCorporateDevice)
            {
                return DetectionInfo?.GetDetectionSummary() ?? "Corporate device detected";
            }

            return "Personal device - Free for personal use";
        }
    }
}
