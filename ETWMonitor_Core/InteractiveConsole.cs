using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using EtwMonitor.Core.Models;
using EtwMonitor.Core.Services;
using EtwMonitor.Core.AI;
using EtwMonitor.Core.Data;
using Serilog;

namespace EtwMonitor.Console
{
    /// <summary>
    /// Interactive console UI for troubleshooting with Copilot
    /// </summary>
    public class InteractiveConsole : BackgroundService
    {
        private readonly MonitoringService _monitoringService;
        private readonly CopilotAnalyzer? _copilotAnalyzer;
        private readonly MonitorDbContext _dbContext;
        private readonly ILogger _logger;
        private readonly List<DetectedPattern> _recentPatterns = new();
        private readonly object _lock = new();
        private bool _showEvents = false;

        public InteractiveConsole(
            MonitoringService monitoringService,
            MonitorDbContext dbContext,
            ILogger logger,
            CopilotAnalyzer? copilotAnalyzer = null)
        {
            _monitoringService = monitoringService;
            _copilotAnalyzer = copilotAnalyzer;
            _dbContext = dbContext;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Subscribe to events
            _monitoringService.PatternDetected += OnPatternDetected;
            _monitoringService.EventProcessed += OnEventProcessed;
            
            // Show interactive menu
            await Task.Run(() => RunInteractiveMenu(stoppingToken), stoppingToken);
        }

        private void OnPatternDetected(object? sender, DetectedPattern pattern)
        {
            lock (_lock)
            {
                _recentPatterns.Add(pattern);
                if (_recentPatterns.Count > 50)
                    _recentPatterns.RemoveAt(0);
            }
            
            // Always show pattern notifications (even if events are hidden)
            System.Console.WriteLine();
            System.Console.ForegroundColor = GetColorForSeverity(pattern.Severity);
            System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠ PATTERN: {pattern.Description}");
            System.Console.WriteLine($"            Severity: {pattern.Severity} | Confidence: {pattern.Confidence:P0}");
            System.Console.ResetColor();
        }

        private void OnEventProcessed(object? sender, SystemEvent evt)
        {
            if (!_showEvents) return;
            
            // Only show errors when event display is enabled
            if (evt.Result == "SUCCESS") return;
            
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"[{evt.Timestamp:HH:mm:ss}] {evt.Type} | {evt.ProcessName} | {evt.Operation} | {evt.Result}");
            System.Console.ResetColor();
        }

        private void RunInteractiveMenu(CancellationToken stoppingToken)
        {
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine("=== INTERACTIVE MODE READY ===");
            System.Console.ResetColor();
            ShowHelp();
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    System.Console.Write("\nETW> ");
                    var input = System.Console.ReadLine();
                    
                    if (string.IsNullOrWhiteSpace(input))
                        continue;
                    
                    var command = input.Trim().ToLower();
                    
                    switch (command)
                    {
                        case "help":
                        case "?":
                            ShowHelp();
                            break;
                            
                        case "patterns":
                        case "p":
                            ShowPatterns();
                            break;
                            
                        case "analyze":
                        case "a":
                            AnalyzeLatestPattern().Wait();
                            break;
                            
                        case "chat":
                        case "c":
                            StartChatSession(stoppingToken).Wait();
                            break;
                            
                        case "stats":
                        case "s":
                            ShowStats();
                            break;
                            
                        case "events on":
                            _showEvents = true;
                            System.Console.WriteLine("Event display: ON (errors only)");
                            break;
                            
                        case "events off":
                            _showEvents = false;
                            System.Console.WriteLine("Event display: OFF");
                            break;
                            
                        case "clear":
                            try { System.Console.Clear(); } catch { }
                            ShowHelp();
                            break;
                            
                        case "exit":
                        case "quit":
                        case "q":
                            System.Console.WriteLine("Stopping monitor...");
                            Environment.Exit(0);
                            break;
                            
                        default:
                            if (command.StartsWith("ask "))
                            {
                                var question = input.Substring(4);
                                AskCopilot(question).Wait();
                            }
                            else
                            {
                                System.Console.WriteLine($"Unknown command: {command}");
                                System.Console.WriteLine("Type 'help' for available commands.");
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error in interactive menu");
                    System.Console.ForegroundColor = ConsoleColor.Red;
                    System.Console.WriteLine($"Error: {ex.Message}");
                    System.Console.ResetColor();
                }
            }
        }

        private void ShowHelp()
        {
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("Available Commands:");
            System.Console.ResetColor();
            System.Console.WriteLine("  patterns (p)     - Show recent detected patterns");
            System.Console.WriteLine("  analyze (a)      - Analyze latest pattern with Copilot");
            System.Console.WriteLine("  chat (c)         - Start interactive chat with Copilot");
            System.Console.WriteLine("  ask <question>   - Ask Copilot a question");
            System.Console.WriteLine("  stats (s)        - Show monitoring statistics");
            System.Console.WriteLine("  events on/off    - Toggle event display");
            System.Console.WriteLine("  clear            - Clear screen");
            System.Console.WriteLine("  help (?)         - Show this help");
            System.Console.WriteLine("  exit (q)         - Stop monitoring");
            System.Console.WriteLine();
        }

        private void ShowPatterns()
        {
            lock (_lock)
            {
                if (_recentPatterns.Count == 0)
                {
                    System.Console.WriteLine("No patterns detected yet.");
                    return;
                }
                
                System.Console.WriteLine();
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine($"=== Recent Patterns ({_recentPatterns.Count}) ===");
                System.Console.ResetColor();
                
                for (int i = _recentPatterns.Count - 1; i >= Math.Max(0, _recentPatterns.Count - 10); i--)
                {
                    var pattern = _recentPatterns[i];
                    System.Console.ForegroundColor = GetColorForSeverity(pattern.Severity);
                    System.Console.WriteLine($"\n[{i + 1}] {pattern.PatternType} - {pattern.Severity}");
                    System.Console.ResetColor();
                    System.Console.WriteLine($"    {pattern.Description}");
                    System.Console.WriteLine($"    Time: {pattern.LastSeen:HH:mm:ss}");
                    System.Console.WriteLine($"    Suggestion: {pattern.Suggestion}");
                    
                    if (!string.IsNullOrEmpty(pattern.RootCause))
                    {
                        System.Console.ForegroundColor = ConsoleColor.Green;
                        System.Console.WriteLine($"    ✓ Analyzed by Copilot");
                        System.Console.ResetColor();
                    }
                }
            }
        }

        private async Task AnalyzeLatestPattern()
        {
            if (_copilotAnalyzer == null)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("Copilot is not configured. Check appsettings.json.");
                System.Console.ResetColor();
                return;
            }
            
            DetectedPattern? pattern;
            lock (_lock)
            {
                pattern = _recentPatterns.LastOrDefault();
            }
            
            if (pattern == null)
            {
                System.Console.WriteLine("No patterns detected yet.");
                return;
            }
            
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("🤖 Analyzing with Copilot...");
            System.Console.ResetColor();
            
            try
            {
                var relatedEvents = _dbContext.Events
                    .Where(e => pattern.RelatedEventIds.Contains(e.Id))
                    .ToList();
                
                var diagnosis = await _copilotAnalyzer.AnalyzePatternAsync(pattern, relatedEvents);
                
                System.Console.WriteLine();
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine("═══ COPILOT DIAGNOSIS ═══");
                System.Console.ResetColor();
                
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.WriteLine("\nROOT CAUSE:");
                System.Console.WriteLine(diagnosis.RootCause);
                
                System.Console.WriteLine("\nREMEDIATION:");
                System.Console.WriteLine(diagnosis.Remediation);
                
                if (diagnosis.PreventionMeasures.Any())
                {
                    System.Console.WriteLine("\nPREVENTION:");
                    foreach (var measure in diagnosis.PreventionMeasures)
                    {
                        System.Console.WriteLine($"  • {measure}");
                    }
                }
                
                System.Console.ResetColor();
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.WriteLine($"\nConfidence: {diagnosis.CopilotConfidence:P0}");
                System.Console.ResetColor();
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine("═════════════════════════");
                System.Console.ResetColor();
            }
            catch (Exception ex)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine($"Error analyzing pattern: {ex.Message}");
                System.Console.ResetColor();
            }
        }

        private async Task AskCopilot(string question)
        {
            if (_copilotAnalyzer == null)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("Copilot is not configured. Check appsettings.json.");
                System.Console.ResetColor();
                return;
            }
            
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("🤖 Asking Copilot...");
            System.Console.ResetColor();
            
            try
            {
                var response = await _copilotAnalyzer.ChatAsync(question);
                
                System.Console.WriteLine();
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.WriteLine(response);
                System.Console.ResetColor();
            }
            catch (Exception ex)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine($"Error: {ex.Message}");
                System.Console.ResetColor();
            }
        }

        private async Task StartChatSession(CancellationToken stoppingToken)
        {
            if (_copilotAnalyzer == null)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("Copilot is not configured. Check appsettings.json.");
                System.Console.ResetColor();
                return;
            }
            
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine("=== COPILOT CHAT SESSION ===");
            System.Console.WriteLine("Type 'exit' to return to main menu");
            System.Console.ResetColor();
            
            var history = new List<string>();
            
            while (!stoppingToken.IsCancellationRequested)
            {
                System.Console.Write("\nYou> ");
                var input = System.Console.ReadLine();
                
                if (string.IsNullOrWhiteSpace(input))
                    continue;
                
                if (input.Trim().ToLower() == "exit")
                    break;
                
                history.Add(input);
                
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.Write("Copilot> ");
                System.Console.ResetColor();
                
                try
                {
                    var response = await _copilotAnalyzer.ChatAsync(input, history.Count > 1 ? history : null);
                    System.Console.WriteLine(response);
                    history.Add(response);
                }
                catch (Exception ex)
                {
                    System.Console.ForegroundColor = ConsoleColor.Red;
                    System.Console.WriteLine($"Error: {ex.Message}");
                    System.Console.ResetColor();
                }
            }
            
            System.Console.WriteLine("\nReturning to main menu...");
        }

        private void ShowStats()
        {
            var stats = _monitoringService.GetStatistics();
            
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("=== MONITORING STATISTICS ===");
            System.Console.ResetColor();
            
            System.Console.WriteLine($"Uptime: {stats.Uptime:hh\\:mm\\:ss}");
            System.Console.WriteLine($"Total Events: {stats.TotalEvents:N0}");
            System.Console.WriteLine($"Total Patterns: {stats.TotalPatterns}");
            System.Console.WriteLine($"Events/Second: {stats.EventsPerSecond:F1}");
            
            if (stats.PatternStats.BySeverity.Any())
            {
                System.Console.WriteLine("\nPatterns by Severity:");
                foreach (var kvp in stats.PatternStats.BySeverity.OrderByDescending(x => x.Key))
                {
                    System.Console.ForegroundColor = GetColorForSeverity(kvp.Key);
                    System.Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
                    System.Console.ResetColor();
                }
            }
            
            lock (_lock)
            {
                System.Console.WriteLine($"\nRecent Patterns (in memory): {_recentPatterns.Count}");
            }
        }

        private ConsoleColor GetColorForSeverity(Severity severity) => severity switch
        {
            Severity.Critical => ConsoleColor.Red,
            Severity.High => ConsoleColor.Yellow,
            Severity.Medium => ConsoleColor.DarkYellow,
            _ => ConsoleColor.Gray
        };
    }
}
