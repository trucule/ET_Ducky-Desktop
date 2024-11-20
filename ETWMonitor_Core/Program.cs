using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using EtwMonitor.Core.Configuration;
using EtwMonitor.Core.Services;
using EtwMonitor.Core.Capture;
using EtwMonitor.Core.Patterns;
using EtwMonitor.Core.AI;
using EtwMonitor.Core.Data;
using EtwMonitor.Core.Models;

namespace EtwMonitor.Console
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File("logs/etwmonitor-.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                Log.Information("ETW Monitor Full - Starting...");
                
                var host = CreateHostBuilder(args).Build();
                
                // Display startup banner
                DisplayBanner(host.Services);
                
                await host.RunAsync();
                
                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureServices((hostContext, services) =>
                {
                    // Load configuration
                    var config = new ConfigurationBuilder()
                        .SetBasePath(Directory.GetCurrentDirectory())
                        .AddJsonFile("appsettings.json", optional: false)
                        .AddJsonFile($"appsettings.{hostContext.HostingEnvironment.EnvironmentName}.json", optional: true)
                        .AddEnvironmentVariables()
                        .Build();

                    var monitorConfig = new MonitorConfiguration();
                    config.Bind(monitorConfig);
                    services.AddSingleton(monitorConfig);

                    // Register database
                    services.AddDbContext<MonitorDbContext>(options =>
                        options.UseSqlite(monitorConfig.Database.ConnectionString));

                    // Register core services
                    services.AddSingleton<ILogger>(Log.Logger);
                    services.AddSingleton<EtwCaptureEngine>();
                    services.AddSingleton<AdvancedPatternDetector>();

                    // Register Copilot analyzer if configured
                    if (!string.IsNullOrEmpty(monitorConfig.Copilot.Endpoint) &&
                        !string.IsNullOrEmpty(monitorConfig.Copilot.ApiKey))
                    {
                        services.AddSingleton(sp => new CopilotAnalyzer(
                            sp.GetRequiredService<ILogger>(),
                            monitorConfig.Copilot.Endpoint,
                            monitorConfig.Copilot.ApiKey,
                            monitorConfig.Copilot.DeploymentName,
                            monitorConfig.Copilot.MaxTokens
                        ));
                    }

                    // Register monitoring service
                    services.AddSingleton<MonitoringService>();
                    services.AddHostedService(sp => sp.GetRequiredService<MonitoringService>());
                    
                    // Register interactive console UI
                    services.AddHostedService<InteractiveConsole>();
                });

        static void DisplayBanner(IServiceProvider services)
        {
            try
            {
                System.Console.Clear();
            }
            catch
            {
                // Ignore console clear errors (happens in some PowerShell environments)
                System.Console.WriteLine();
            }
            
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine(@"
╔═══════════════════════════════════════════════════════════════╗
║                                                               ║
║     ███████╗████████╗██╗    ██╗    ███╗   ███╗ ██████╗       ║
║     ██╔════╝╚══██╔══╝██║    ██║    ████╗ ████║██╔═══██╗      ║
║     █████╗     ██║   ██║ █╗ ██║    ██╔████╔██║██║   ██║      ║
║     ██╔══╝     ██║   ██║███╗██║    ██║╚██╔╝██║██║   ██║      ║
║     ███████╗   ██║   ╚███╔███╔╝    ██║ ╚═╝ ██║╚██████╔╝      ║
║     ╚══════╝   ╚═╝    ╚══╝╚══╝     ╚═╝     ╚═╝ ╚═════╝       ║
║                                                               ║
║            Real-Time System Monitor with AI Analysis         ║
║                     Production Version 1.0                    ║
║                                                               ║
╚═══════════════════════════════════════════════════════════════╝
");
            System.Console.ResetColor();
            System.Console.WriteLine();
            
            var config = services.GetRequiredService<MonitorConfiguration>();
            var copilot = services.GetService<CopilotAnalyzer>();
            
            System.Console.WriteLine("Configuration:");
            System.Console.WriteLine($"  ETW Monitoring: {GetEnabledFeatures(config)}");
            System.Console.WriteLine($"  Copilot AI: {(copilot != null ? "ENABLED" : "DISABLED")}");
            System.Console.WriteLine($"  Database: {(config.Database.EnablePersistence ? "ENABLED" : "DISABLED")}");
            System.Console.WriteLine();
            System.Console.WriteLine("Press Ctrl+C to stop monitoring");
            System.Console.WriteLine(new string('─', 65));
            System.Console.WriteLine();
        }

        static string GetEnabledFeatures(MonitorConfiguration config)
        {
            var features = new System.Collections.Generic.List<string>();
            if (config.Etw.EnableFileSystem) features.Add("FileSystem");
            if (config.Etw.EnableRegistry) features.Add("Registry");
            if (config.Etw.EnableProcess) features.Add("Process");
            if (config.Etw.EnableNetwork) features.Add("Network");
            return string.Join(", ", features);
        }
    }

    // Simple console UI that displays events in real-time
    public class ConsoleUIService : BackgroundService
    {
        private readonly MonitoringService _monitoringService;
        private readonly ILogger _logger;

        public ConsoleUIService(MonitoringService monitoringService, ILogger logger)
        {
            _monitoringService = monitoringService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _monitoringService.EventProcessed += OnEventProcessed;
            _monitoringService.PatternDetected += OnPatternDetected;
            _monitoringService.DiagnosisCompleted += OnDiagnosisCompleted;
            
            // Keep the service running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private void OnEventProcessed(object? sender, SystemEvent evt)
        {
            // Only display errors and important events to avoid console spam
            if (evt.Result == "SUCCESS")
                return;
            
            System.Console.ForegroundColor = GetColorForEventType(evt.Type);
            System.Console.Write($"[{evt.Timestamp:HH:mm:ss.fff}] ");
            System.Console.Write($"{evt.Type} | {evt.ProcessName} | {evt.Operation}");
            
            if (!string.IsNullOrEmpty(evt.Path))
                System.Console.Write($" | {TruncatePath(evt.Path, 40)}");
            
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($" | {evt.Result}");
            System.Console.ResetColor();
        }

        private void OnPatternDetected(object? sender, DetectedPattern pattern)
        {
            System.Console.WriteLine();
            System.Console.ForegroundColor = GetColorForSeverity(pattern.Severity);
            System.Console.WriteLine($"⚠ PATTERN DETECTED: {pattern.PatternType}");
            System.Console.WriteLine($"   {pattern.Description}");
            System.Console.WriteLine($"   Severity: {pattern.Severity} | Confidence: {pattern.Confidence:P0}");
            System.Console.WriteLine($"   Suggestion: {pattern.Suggestion}");
            
            if (pattern.Severity >= Severity.High)
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.WriteLine($"   🤖 Copilot will analyze this pattern...");
            }
            
            System.Console.ResetColor();
            System.Console.WriteLine();
        }

        private void OnDiagnosisCompleted(object? sender, Diagnosis diagnosis)
        {
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("═══ COPILOT DIAGNOSIS ═══");
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine($"ROOT CAUSE:");
            System.Console.WriteLine($"  {diagnosis.RootCause}");
            System.Console.WriteLine();
            System.Console.WriteLine($"REMEDIATION:");
            System.Console.WriteLine($"  {diagnosis.Remediation}");
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"Confidence: {diagnosis.CopilotConfidence:P0}");
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("═════════════════════════");
            System.Console.ResetColor();
            System.Console.WriteLine();
        }

        private ConsoleColor GetColorForEventType(EventType type) => type switch
        {
            EventType.FileSystem => ConsoleColor.Green,
            EventType.Registry => ConsoleColor.Blue,
            EventType.Process => ConsoleColor.Magenta,
            EventType.Network => ConsoleColor.Cyan,
            _ => ConsoleColor.White
        };

        private ConsoleColor GetColorForSeverity(Severity severity) => severity switch
        {
            Severity.Critical => ConsoleColor.Red,
            Severity.High => ConsoleColor.Yellow,
            Severity.Medium => ConsoleColor.DarkYellow,
            _ => ConsoleColor.Gray
        };

        private string TruncatePath(string path, int maxLength)
        {
            if (path.Length <= maxLength)
                return path;
            
            return "..." + path.Substring(path.Length - maxLength + 3);
        }
    }
}
