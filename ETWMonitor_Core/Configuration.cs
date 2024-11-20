using System.Collections.Generic;

namespace EtwMonitor.Core.Configuration
{
    public class MonitorConfiguration
    {
        public EtwSettings Etw { get; set; } = new();
        public CopilotSettings Copilot { get; set; } = new();
        public DatabaseSettings Database { get; set; } = new();
        public FilterSettings Filters { get; set; } = new();
        public AlertSettings Alerts { get; set; } = new();
    }

    public class EtwSettings
    {
        public bool EnableFileSystem { get; set; } = true;
        public bool EnableRegistry { get; set; } = true;
        public bool EnableProcess { get; set; } = true;
        public bool EnableNetwork { get; set; } = true;
        public int EventBufferSize { get; set; } = 10000;
        public int ProcessingThreads { get; set; } = 4;
    }

    public class CopilotSettings
    {
        public string Endpoint { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string DeploymentName { get; set; } = "gpt-4";
        public int MaxTokens { get; set; } = 2000;
        public bool AutoAnalyze { get; set; } = true;
        public int MinSeverityForAnalysis { get; set; } = 2; // Medium or higher
    }

    public class DatabaseSettings
    {
        public string ConnectionString { get; set; } = "Data Source=etwmonitor.db";
        public int RetentionDays { get; set; } = 30;
        public bool EnablePersistence { get; set; } = true;
    }

    public class FilterSettings
    {
        public List<string> IgnorePaths { get; set; } = new()
        {
            "System Volume Information",
            "pagefile.sys",
            "$Recycle.Bin",
            "WindowsApps",
            "AppData\\Local\\Temp",
            "Windows\\Temp"
        };
        
        public List<string> IgnoreProcesses { get; set; } = new()
        {
            "svchost.exe",
            "System",
            "Idle"
        };
        
        public bool FilterSuccessfulOperations { get; set; } = true;
        public int MinEventDurationMs { get; set; } = 0;
    }

    public class AlertSettings
    {
        public bool EnableEmail { get; set; } = false;
        public string SmtpServer { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 587;
        public string FromAddress { get; set; } = string.Empty;
        public List<string> ToAddresses { get; set; } = new();
        
        public bool EnableWebhook { get; set; } = false;
        public string WebhookUrl { get; set; } = string.Empty;
        
        public int MinSeverityForAlert { get; set; } = 3; // High or Critical
    }
}
