using System.Text.Json;

namespace EtwMonitor.Desktop.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings _settings;

    public event EventHandler<AppSettings>? SettingsChanged;

    public SettingsService()
    {
        // Use proper app data folder
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appFolder = Path.Combine(appData, "ETWMonitor");
        
        // Create folder if it doesn't exist
        if (!Directory.Exists(appFolder))
        {
            Directory.CreateDirectory(appFolder);
            System.Diagnostics.Debug.WriteLine($"Created settings directory: {appFolder}");
        }
        
        _settingsPath = Path.Combine(appFolder, "appsettings.json");
        System.Diagnostics.Debug.WriteLine($"Settings path: {_settingsPath}");
        
        _settings = LoadSettings();
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                };
                var settings = JsonSerializer.Deserialize<AppSettings>(json, options);
                
                if (settings == null)
                {
                    System.Diagnostics.Debug.WriteLine("Settings deserialized to null, returning defaults");
                    return CreateDefaultSettings();
                }
                
                // Ensure nested objects are initialized
                settings.Monitoring ??= new MonitoringConfiguration();
                settings.AIProviders ??= new AIProvidersConfiguration();
                settings.Database ??= new DatabaseConfiguration();
                
                System.Diagnostics.Debug.WriteLine($"Loaded settings from file:");
                System.Diagnostics.Debug.WriteLine($"  FS: {settings.Monitoring.EnableFileSystem}");
                System.Diagnostics.Debug.WriteLine($"  Reg: {settings.Monitoring.EnableRegistry}");
                System.Diagnostics.Debug.WriteLine($"  Proc: {settings.Monitoring.EnableProcess}");
                System.Diagnostics.Debug.WriteLine($"  Net: {settings.Monitoring.EnableNetwork}");
                System.Diagnostics.Debug.WriteLine($"  SkipOwn: {settings.Monitoring.SkipOwnProcessEvents}");

                return settings;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Settings file does not exist, creating with defaults");
                var defaultSettings = CreateDefaultSettings();
                
                // Save defaults immediately
                var json = JsonSerializer.Serialize(defaultSettings, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_settingsPath, json);
                System.Diagnostics.Debug.WriteLine($"Default settings file created at: {_settingsPath}");
                
                return defaultSettings;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            return CreateDefaultSettings();
        }
    }

    private AppSettings CreateDefaultSettings()
    {
        var settings = new AppSettings();
        System.Diagnostics.Debug.WriteLine("Created default settings with all monitoring ENABLED");
        return settings;
    }

    public AppSettings GetSettings()
    {
        // Reload settings from disk to ensure we have the latest
        _settings = LoadSettings();
        return _settings;
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("=== Saving Settings ===");
            System.Diagnostics.Debug.WriteLine($"  FS: {settings.Monitoring.EnableFileSystem}");
            System.Diagnostics.Debug.WriteLine($"  Reg: {settings.Monitoring.EnableRegistry}");
            System.Diagnostics.Debug.WriteLine($"  Proc: {settings.Monitoring.EnableProcess}");
            System.Diagnostics.Debug.WriteLine($"  Net: {settings.Monitoring.EnableNetwork}");
            System.Diagnostics.Debug.WriteLine($"  SkipOwn: {settings.Monitoring.SkipOwnProcessEvents}");

            _settings = settings;
            
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            });
            
            System.Diagnostics.Debug.WriteLine($"Writing to: {_settingsPath}");
            System.Diagnostics.Debug.WriteLine($"JSON content:\n{json}");
            
            await File.WriteAllTextAsync(_settingsPath, json);
            
            // Verify the write
            if (File.Exists(_settingsPath))
            {
                var verifyJson = await File.ReadAllTextAsync(_settingsPath);
                System.Diagnostics.Debug.WriteLine("Settings file written successfully");
                System.Diagnostics.Debug.WriteLine($"File size: {new FileInfo(_settingsPath).Length} bytes");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("WARNING: Settings file does not exist after write!");
            }
            
            SettingsChanged?.Invoke(this, settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    public MonitoringConfiguration GetMonitoringConfiguration()
    {
        return _settings.Monitoring;
    }

    public AIProvidersConfiguration GetAIProvidersConfiguration()
    {
        return _settings.AIProviders;
    }

    /// <summary>
    /// Gets the full path where settings are stored
    /// </summary>
    public string GetSettingsPath()
    {
        return _settingsPath;
    }
}

public class AppSettings
{
    public MonitoringConfiguration Monitoring { get; set; } = new();
    public AIProvidersConfiguration AIProviders { get; set; } = new();
    public DatabaseConfiguration Database { get; set; } = new();
}

public class MonitoringConfiguration
{
    public bool EnableFileSystem { get; set; } = true;
    public bool EnableRegistry { get; set; } = true;
    public bool EnableProcess { get; set; } = true;
    public bool EnableNetwork { get; set; } = true;
    public bool SkipOwnProcessEvents { get; set; } = true;
}

public class DatabaseConfiguration
{
    public string ConnectionString { get; set; }
    public bool EnablePersistence { get; set; } = true;

    public DatabaseConfiguration()
    {
        // Store the DB in the same LocalAppData folder as settings
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "ETWMonitor");

        if (!Directory.Exists(appFolder))
        {
            Directory.CreateDirectory(appFolder);
        }

        var dbPath = Path.Combine(appFolder, "etwmonitor.db");
        ConnectionString = $"Data Source={dbPath}";
    }
}