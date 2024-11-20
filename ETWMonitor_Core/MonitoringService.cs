using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using EtwMonitor.Core.Models;
using EtwMonitor.Core.Capture;
using EtwMonitor.Core.Patterns;
using EtwMonitor.Core.AI;
using EtwMonitor.Core.Data;
using EtwMonitor.Core.Configuration;
using Serilog;

namespace EtwMonitor.Core.Services
{
    public class MonitoringService : IHostedService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly MonitorConfiguration _config;
        private readonly MonitorDbContext _dbContext;
        private readonly EtwCaptureEngine _captureEngine;
        private readonly AdvancedPatternDetector _patternDetector;
        private readonly CopilotAnalyzer? _copilotAnalyzer;
        
        private readonly Timer _cleanupTimer;
        private long _totalEventsProcessed = 0;
        private long _totalPatternsDetected = 0;
        private DateTime _startTime;

        public event EventHandler<SystemEvent>? EventProcessed;
        public event EventHandler<DetectedPattern>? PatternDetected;
        public event EventHandler<Diagnosis>? DiagnosisCompleted;

        public MonitoringService(
            ILogger logger,
            MonitorConfiguration config,
            MonitorDbContext dbContext,
            EtwCaptureEngine captureEngine,
            AdvancedPatternDetector patternDetector,
            CopilotAnalyzer? copilotAnalyzer = null)
        {
            _logger = logger;
            _config = config;
            _dbContext = dbContext;
            _captureEngine = captureEngine;
            _patternDetector = patternDetector;
            _copilotAnalyzer = copilotAnalyzer;
            
            // Set up cleanup timer (runs every hour)
            _cleanupTimer = new Timer(CleanupOldData, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.Information("=== ETW Monitor Full - Production Version ===");
            _logger.Information("Starting Monitoring Service...");
            
            _startTime = DateTime.Now;
            
            // Initialize database
            try
            {
                if (_config.Database.EnablePersistence)
                {
                    await _dbContext.Database.EnsureCreatedAsync(cancellationToken);
                    _logger.Information("Database initialized");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize database");
            }
            
            // Wire up event handlers
            _captureEngine.EventCaptured += OnEventCaptured;
            _captureEngine.ErrorOccurred += OnCaptureError;
            
            // Start ETW capture
            await _captureEngine.StartAsync(cancellationToken);
            
            _logger.Information("Monitoring Service started successfully");
            _logger.Information("Configuration:");
            _logger.Information("  - File System: {Enabled}", _config.Etw.EnableFileSystem);
            _logger.Information("  - Registry: {Enabled}", _config.Etw.EnableRegistry);
            _logger.Information("  - Process: {Enabled}", _config.Etw.EnableProcess);
            _logger.Information("  - Network: {Enabled}", _config.Etw.EnableNetwork);
            _logger.Information("  - Copilot: {Enabled}", _copilotAnalyzer != null);
            _logger.Information("  - Database: {Enabled}", _config.Database.EnablePersistence);
        }

        private async void OnEventCaptured(object? sender, SystemEvent evt)
        {
            try
            {
                Interlocked.Increment(ref _totalEventsProcessed);
                
                // Apply filters
                if (ShouldFilterEvent(evt))
                    return;
                
                // Save to database
                if (_config.Database.EnablePersistence)
                {
                    _dbContext.Events.Add(evt);
                    
                    // Batch saves for performance
                    if (_totalEventsProcessed % 100 == 0)
                    {
                        await _dbContext.SaveChangesAsync();
                    }
                }
                
                // Pattern detection
                var patterns = _patternDetector.AnalyzeEvent(evt);
                
                foreach (var pattern in patterns)
                {
                    await HandleDetectedPattern(pattern);
                }
                
                // Raise event for UI
                EventProcessed?.Invoke(this, evt);
                
                // Log progress periodically
                if (_totalEventsProcessed % 10000 == 0)
                {
                    _logger.Information("Processed {Count} events, {Patterns} patterns detected", 
                        _totalEventsProcessed, _totalPatternsDetected);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing event");
            }
        }

        private async Task HandleDetectedPattern(DetectedPattern pattern)
        {
            try
            {
                Interlocked.Increment(ref _totalPatternsDetected);
                
                // Save pattern to database
                if (_config.Database.EnablePersistence)
                {
                    _dbContext.Patterns.Add(pattern);
                    await _dbContext.SaveChangesAsync();
                }
                
                // Raise event for UI
                PatternDetected?.Invoke(this, pattern);
                
                // Copilot analysis (if enabled and meets severity threshold)
                if (_copilotAnalyzer != null && 
                    _config.Copilot.AutoAnalyze &&
                    (int)pattern.Severity >= _config.Copilot.MinSeverityForAnalysis)
                {
                    await AnalyzePatternWithCopilot(pattern);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling detected pattern");
            }
        }

        private async Task AnalyzePatternWithCopilot(DetectedPattern pattern)
        {
            try
            {
                _logger.Information("Sending pattern to Copilot for analysis: {Pattern}", pattern.Description);
                
                // Get related events from database
                var relatedEvents = await _dbContext.Events
                    .Where(e => pattern.RelatedEventIds.Contains(e.Id))
                    .ToListAsync();
                
                // Analyze with Copilot
                var diagnosis = await _copilotAnalyzer!.AnalyzePatternAsync(pattern, relatedEvents);
                
                // Update pattern with diagnosis
                pattern.CopilotAnalysis = diagnosis.AdditionalContext;
                pattern.RootCause = diagnosis.RootCause;
                pattern.Remediation = diagnosis.Remediation;
                pattern.AnalyzedAt = DateTime.Now;
                
                // Save diagnosis
                if (_config.Database.EnablePersistence)
                {
                    _dbContext.Diagnoses.Add(diagnosis);
                    await _dbContext.SaveChangesAsync();
                }
                
                _logger.Information("Copilot analysis complete. Confidence: {Confidence:P0}", diagnosis.CopilotConfidence);
                
                // Raise event for UI
                DiagnosisCompleted?.Invoke(this, diagnosis);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error analyzing pattern with Copilot");
            }
        }

        private bool ShouldFilterEvent(SystemEvent evt)
        {
            // Filter successful operations if configured
            if (_config.Filters.FilterSuccessfulOperations && evt.Result == "SUCCESS")
                return true;
            
            // Filter ignored paths
            if (evt.Path != null && _config.Filters.IgnorePaths.Any(p => evt.Path.Contains(p)))
                return true;
            
            // Filter ignored processes
            if (evt.ProcessName != null && _config.Filters.IgnoreProcesses.Any(p => evt.ProcessName.Contains(p)))
                return true;
            
            // Filter by duration
            if (evt.Duration.HasValue && 
                evt.Duration.Value / 1000 < _config.Filters.MinEventDurationMs)
                return true;
            
            return false;
        }

        private void OnCaptureError(object? sender, string error)
        {
            _logger.Error("ETW Capture Error: {Error}", error);
        }

        private async void CleanupOldData(object? state)
        {
            try
            {
                if (!_config.Database.EnablePersistence)
                    return;
                
                var cutoffDate = DateTime.Now.AddDays(-_config.Database.RetentionDays);
                
                _logger.Information("Cleaning up data older than {Date}", cutoffDate);
                
                var oldEvents = await _dbContext.Events
                    .Where(e => e.Timestamp < cutoffDate)
                    .ToListAsync();
                
                if (oldEvents.Any())
                {
                    _dbContext.Events.RemoveRange(oldEvents);
                    await _dbContext.SaveChangesAsync();
                    
                    _logger.Information("Cleaned up {Count} old events", oldEvents.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during cleanup");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.Information("Stopping Monitoring Service...");
            
            await _captureEngine.StopAsync();
            
            // Final database save
            if (_config.Database.EnablePersistence)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            
            var duration = DateTime.Now - _startTime;
            _logger.Information("=== Monitoring Session Summary ===");
            _logger.Information("Duration: {Duration}", duration);
            _logger.Information("Total Events: {Count}", _totalEventsProcessed);
            _logger.Information("Total Patterns: {Count}", _totalPatternsDetected);
            _logger.Information("Events/Second: {Rate:F1}", _totalEventsProcessed / duration.TotalSeconds);
            
            _logger.Information("Monitoring Service stopped");
        }

        public MonitorStatistics GetStatistics()
        {
            var uptime = DateTime.Now - _startTime;
            
            return new MonitorStatistics
            {
                Uptime = uptime,
                TotalEvents = _totalEventsProcessed,
                TotalPatterns = _totalPatternsDetected,
                EventsPerSecond = _totalEventsProcessed / uptime.TotalSeconds,
                PatternStats = _patternDetector.GetStatistics()
            };
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _captureEngine?.Dispose();
        }
    }

    public class MonitorStatistics
    {
        public TimeSpan Uptime { get; set; }
        public long TotalEvents { get; set; }
        public long TotalPatterns { get; set; }
        public double EventsPerSecond { get; set; }
        public PatternStatistics PatternStats { get; set; } = new();
    }
}
