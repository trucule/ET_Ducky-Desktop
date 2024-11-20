using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EtwMonitor.Desktop.Models;

namespace EtwMonitor.Desktop.Services
{
    /// <summary>
    /// Service for validating licenses in the client application
    /// </summary>
    public class LicenseValidationService
    {
        private readonly byte[] _publicKey;
        private readonly string _licenseFilePath;
        private readonly DomainDetectionService _domainService;
        private const string LicenseFileName = "license.dat";

        public LicenseValidationService(string publicKeyBase64, DomainDetectionService domainService)
        {
            _publicKey = Convert.FromBase64String(publicKeyBase64);
            _domainService = domainService;

            var dataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ETDucky"
            );
            Directory.CreateDirectory(dataPath);
            _licenseFilePath = Path.Combine(dataPath, LicenseFileName);
        }

        /// <summary>
        /// Gets the current machine ID for license binding
        /// </summary>
        public string GetMachineId()
        {
            try
            {
                // Use a combination of hardware identifiers
                var cpuId = GetCpuId();
                var motherboardId = GetMotherboardId();
                var combined = $"{cpuId}-{motherboardId}";

                using (var sha256 = SHA256.Create())
                {
                    var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
                    return Convert.ToBase64String(hash).Substring(0, 32);
                }
            }
            catch
            {
                // Fallback to machine name if hardware ID fails
                return Environment.MachineName;
            }
        }

        private string GetCpuId()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        return mo["ProcessorId"]?.ToString() ?? "";
                    }
                }
            }
            catch { }
            return "";
        }

        private string GetMotherboardId()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        return mo["SerialNumber"]?.ToString() ?? "";
                    }
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// Validates the current license status
        /// </summary>
        public LicenseValidationResult ValidateLicense()
        {
            // First check if device qualifies for free usage (personal device)
            var domainStatus = _domainService.GetDomainStatus();
            
            if (domainStatus.QualifiesForFree)
            {
                // Clear any existing grace period since it's a personal device
                _domainService.ClearGracePeriod();
                
                return new LicenseValidationResult
                {
                    IsValid = true,
                    Status = LicenseStatus.Valid,
                    License = new License
                    {
                        Type = LicenseType.Free,
                        IssuedDate = DateTime.UtcNow,
                        ExpiryDate = DateTime.MaxValue
                    },
                    Message = "Personal Use",
                    RequiresDomainCheck = false
                };
            }

            // Device is corporate - check for existing license
            if (File.Exists(_licenseFilePath))
            {
                try
                {
                    var licenseKey = File.ReadAllText(_licenseFilePath);
                    var result = ValidateLicenseKey(licenseKey);
                    
                    if (result.IsValid)
                    {
                        // Valid license found - clear grace period
                        _domainService.ClearGracePeriod();
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    // Continue to grace period check
                }
            }

            // No valid license found on corporate device - check grace period
            if (domainStatus.IsInGracePeriod)
            {
                // Grace period is active
                return new LicenseValidationResult
                {
                    IsValid = true,
                    Status = LicenseStatus.Valid,
                    License = new License
                    {
                        Type = LicenseType.Trial,
                        IssuedDate = domainStatus.GracePeriodStart ?? DateTime.UtcNow,
                        ExpiryDate = domainStatus.GracePeriodExpiry ?? DateTime.UtcNow.AddDays(30)
                    },
                    Message = $"Grace Period - {domainStatus.GracePeriodDaysRemaining} days remaining",
                    RequiresDomainCheck = true,
                    IsGracePeriod = true,
                    GracePeriodDaysRemaining = domainStatus.GracePeriodDaysRemaining,
                    DetectionInfo = domainStatus.DetectionInfo
                };
            }

            // Corporate device detected but no grace period - start one
            _domainService.StartGracePeriod();
            var newStatus = _domainService.GetDomainStatus();

            return new LicenseValidationResult
            {
                IsValid = true,
                Status = LicenseStatus.Valid,
                License = new License
                {
                    Type = LicenseType.Trial,
                    IssuedDate = DateTime.UtcNow,
                    ExpiryDate = DateTime.UtcNow.AddDays(30)
                },
                Message = "Grace Period Started - 30 days remaining",
                RequiresDomainCheck = true,
                IsGracePeriod = true,
                GracePeriodDaysRemaining = 30,
                DetectionInfo = newStatus.DetectionInfo
            };
        }

        /// <summary>
        /// Validates a license key
        /// </summary>
        public LicenseValidationResult ValidateLicenseKey(string licenseKey)
        {
            try
            {
                // Remove separators and decode
                var cleanKey = licenseKey.Replace("-", "").Replace(" ", "");
                var jsonBytes = Convert.FromBase64String(cleanKey);
                var json = Encoding.UTF8.GetString(jsonBytes);

                var license = JsonSerializer.Deserialize<License>(json);
                
                if (license == null)
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        Status = LicenseStatus.Invalid,
                        Message = "Invalid license format"
                    };
                }

                // Verify signature
                if (!VerifySignature(license))
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        Status = LicenseStatus.Invalid,
                        Message = "License signature verification failed"
                    };
                }

                // Check if license is bound to this machine
                var currentMachineId = GetMachineId();
                if (!string.IsNullOrEmpty(license.MachineId) && license.MachineId != currentMachineId)
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        Status = LicenseStatus.Invalid,
                        Message = "License is not valid for this machine"
                    };
                }

                // Check license status
                var status = license.GetStatus();
                var isValid = status == LicenseStatus.Valid;

                return new LicenseValidationResult
                {
                    IsValid = isValid,
                    Status = status,
                    License = license,
                    Message = GetStatusMessage(license, status)
                };
            }
            catch (Exception ex)
            {
                return new LicenseValidationResult
                {
                    IsValid = false,
                    Status = LicenseStatus.Invalid,
                    Message = $"License validation error: {ex.Message}"
                };
            }
        }

        private bool VerifySignature(License license)
        {
            try
            {
                var licenseData = new
                {
                    license.LicenseKey,
                    Type = (int)license.Type,
                    Issued = license.IssuedDate.ToString("O"),
                    Expiry = license.ExpiryDate.ToString("O"),
                    license.Email,
                    license.MachineId
                };

                var json = JsonSerializer.Serialize(licenseData);
                var jsonBytes = Encoding.UTF8.GetBytes(json);
                var signature = Convert.FromBase64String(license.Signature);

                using (var rsa = RSA.Create())
                {
                    rsa.ImportRSAPublicKey(_publicKey, out _);
                    return rsa.VerifyData(jsonBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch
            {
                return false;
            }
        }

        private string GetStatusMessage(License license, LicenseStatus status)
        {
            switch (status)
            {
                case LicenseStatus.Valid:
                    if (license.Type == LicenseType.Lifetime)
                        return "Lifetime license active";
                    if (license.Type == LicenseType.Free)
                        return "Free usage - Device not domain-joined";
                    return $"{license.Type} license active - {license.DaysRemaining()} days remaining";

                case LicenseStatus.Expired:
                    return $"{license.Type} license expired";

                case LicenseStatus.Revoked:
                    return "License has been revoked";

                default:
                    return "Invalid license";
            }
        }

        /// <summary>
        /// Activates a license by saving it to disk
        /// </summary>
        public LicenseValidationResult ActivateLicense(string licenseKey)
        {
            var result = ValidateLicenseKey(licenseKey);
            
            if (result.IsValid)
            {
                File.WriteAllText(_licenseFilePath, licenseKey);
                
                // Clear grace period since we now have a valid license
                _domainService.ClearGracePeriod();
            }

            return result;
        }

        /// <summary>
        /// Removes the current license
        /// </summary>
        public void DeactivateLicense()
        {
            if (File.Exists(_licenseFilePath))
            {
                File.Delete(_licenseFilePath);
            }
        }

        /// <summary>
        /// Gets the current license if one exists
        /// </summary>
        public License? GetCurrentLicense()
        {
            var result = ValidateLicense();
            return result.License;
        }
    }
}
