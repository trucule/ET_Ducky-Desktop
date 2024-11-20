using EtwMonitor.Core.Models;
using EtwMonitor.Core.Capture;
using EtwMonitor.Core.Configuration;
using Serilog;
using System.Collections.Concurrent;

namespace EtwMonitor.Desktop.Services;

public class MonitorStateService
{
    private readonly SettingsService _settingsService;
    private EtwCaptureEngine? _captureEngine;
    private DateTime _startTime;
    private int _totalEvents;
    private readonly List<DetectedPattern> _patterns = new();
    private const string OwnProcessName = "ET_Ducky";

    // OPTIMIZED: Use ConcurrentQueue for thread-safe, efficient event storage
    private readonly ConcurrentQueue<SystemEvent> _capturedEvents = new();
    private const int MaxStoredEvents = 200000; // keeps only recent events
    private int _currentEventCount = 0;
    
    private ILogger? _logger;
    private static string _logFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ETWMonitor_Debug.log");

    // PERFORMANCE: Event pruning settings
    private DateTime _lastPruneTime = DateTime.Now;
    private const int PruneIntervalSeconds = 10;
    private readonly object _pruneLock = new();

    private void Log(string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var line = $"[{timestamp}] {message}\r\n";
            File.AppendAllText(_logFile, line);
        }
        catch { }
    }

    public bool IsMonitoring { get; private set; }

    public event EventHandler<bool>? MonitoringStateChanged;
    public event EventHandler<DetectedPattern>? PatternDetected;
    public event EventHandler<string>? ErrorOccurred;

    public MonitorStateService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        
        // Initialize Serilog logger - using Console sink only
        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger();
    }

    public async Task StartMonitoringAsync()
    {
        if (IsMonitoring) return;

        try
        {
            // Get current settings
            var settings = _settingsService.GetSettings();
            
            System.Diagnostics.Debug.WriteLine("=== Starting Monitoring ===");
            System.Diagnostics.Debug.WriteLine($"File System Monitoring: {settings.Monitoring.EnableFileSystem}");
            System.Diagnostics.Debug.WriteLine($"Registry Monitoring: {settings.Monitoring.EnableRegistry}");
            System.Diagnostics.Debug.WriteLine($"Process Monitoring: {settings.Monitoring.EnableProcess}");
            System.Diagnostics.Debug.WriteLine($"Network Monitoring: {settings.Monitoring.EnableNetwork}");

            // Create ETW capture engine
            _captureEngine = new EtwCaptureEngine(_logger!);
            
            // Wire up events
            _captureEngine.EventCaptured += OnEventReceived;
            _captureEngine.ErrorOccurred += OnCaptureError;

            _startTime = DateTime.Now;
            _totalEvents = 0;
            _currentEventCount = 0;
            _patterns.Clear();
            
            // Clear event queue
            while (_capturedEvents.TryDequeue(out _)) { }

            // Start the capture engine
            await _captureEngine.StartAsync();
            
            IsMonitoring = true;
            MonitoringStateChanged?.Invoke(this, true);
            
            System.Diagnostics.Debug.WriteLine("Monitoring started successfully!");
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Authorization error: {ex.Message}");
            ErrorOccurred?.Invoke(this, "Administrator privileges required. Please run as Administrator.");
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error starting monitoring: {ex.Message}");
            ErrorOccurred?.Invoke(this, $"Failed to start monitoring: {ex.Message}");
            throw;
        }
    }

    public async Task StopMonitoringAsync()
    {
        Log($"[MonitorState] StopMonitoringAsync called - IsMonitoring: {IsMonitoring}");
        
        if (!IsMonitoring)
        {
            Log("[MonitorState] StopMonitoringAsync - Not monitoring, returning");
            return;
        }

        // IMMEDIATELY set state to false and fire event BEFORE trying to stop
        IsMonitoring = false;
        Log($"[MonitorState] StopMonitoringAsync - IMMEDIATELY set IsMonitoring to false");
        
        // Fire the event FIRST so UI updates right away
        Log("[MonitorState] StopMonitoringAsync - Invoking MonitoringStateChanged event IMMEDIATELY");
        MonitoringStateChanged?.Invoke(this, false);
        Log("[MonitorState] StopMonitoringAsync - Event invoked");

        // Now try to cleanup in the background
        try
        {
            Log("[MonitorState] StopMonitoringAsync - Beginning cleanup");
            
            if (_captureEngine != null)
            {
                var engineToClean = _captureEngine;
                _captureEngine = null; // Clear reference immediately
                
                Log("[MonitorState] StopMonitoringAsync - Unhooking event handlers");
                engineToClean.EventCaptured -= OnEventReceived;
                engineToClean.ErrorOccurred -= OnCaptureError;
                
                // Do cleanup in background task with timeout
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Log("[MonitorState] Background cleanup - Calling StopAsync with 5 second timeout");
                        
                        var stopTask = engineToClean.StopAsync();
                        var timeoutTask = Task.Delay(5000);
                        var completedTask = await Task.WhenAny(stopTask, timeoutTask);
                        
                        if (completedTask == stopTask)
                        {
                            Log("[MonitorState] Background cleanup - StopAsync completed successfully");
                        }
                        else
                        {
                            Log("[MonitorState] Background cleanup - StopAsync TIMED OUT after 5 seconds");
                        }
                        
                        Log("[MonitorState] Background cleanup - Disposing engine");
                        engineToClean.Dispose();
                        Log("[MonitorState] Background cleanup - Cleanup complete");
                    }
                    catch (Exception ex)
                    {
                        Log($"[MonitorState] Background cleanup - Error: {ex.Message}");
                    }
                });
                
                Log("[MonitorState] StopMonitoringAsync - Background cleanup started");
            }
            
            Log($"[MonitorState] StopMonitoringAsync - Returning (total events captured: {_totalEvents})");
        }
        catch (Exception ex)
        {
            Log($"[MonitorState] StopMonitoringAsync - EXCEPTION: {ex.Message}");
        }
    }

    // OPTIMIZED: Event capture with automatic pruning
    private void OnEventReceived(object? sender, SystemEvent e)
    {
        try
        {
            var settings = _settingsService.GetSettings();
            
            // Filter events based on settings
            bool shouldCapture = e.Type switch
            {
                EventType.FileSystem => settings.Monitoring.EnableFileSystem,
                EventType.Registry => settings.Monitoring.EnableRegistry,
                EventType.Process => settings.Monitoring.EnableProcess,
                EventType.Network => settings.Monitoring.EnableNetwork,
                _ => false
            };

            if (!shouldCapture)
                return;

            if (settings.Monitoring.SkipOwnProcessEvents)
            {
                var processName = e.ProcessName ?? string.Empty;

                if (!string.IsNullOrEmpty(processName))
                {
                    // Match ET_Ducky or ET_Ducky.exe, case-insensitive
                    if (processName.Equals(OwnProcessName, StringComparison.OrdinalIgnoreCase) ||
                        processName.Equals(OwnProcessName + ".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }

            _totalEvents++;
            
            // Add to queue
            _capturedEvents.Enqueue(e);
            Interlocked.Increment(ref _currentEventCount);
            
            // PERFORMANCE: Prune old events periodically to prevent memory bloat
            if ((DateTime.Now - _lastPruneTime).TotalSeconds > PruneIntervalSeconds)
            {
                PruneOldEvents();
            }
            
            // Log every 1000 events
            if (_totalEvents % 1000 == 0)
            {
                System.Diagnostics.Debug.WriteLine($"Events captured: {_totalEvents} (stored: {_currentEventCount})");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error processing event: {ex.Message}");
        }
    }

    // PERFORMANCE: Remove old events to keep memory usage reasonable
    private void PruneOldEvents()
    {
        lock (_pruneLock)
        {
            _lastPruneTime = DateTime.Now;
            
            // Keep only the most recent events
            while (_currentEventCount > MaxStoredEvents)
            {
                if (_capturedEvents.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref _currentEventCount);
                }
                else
                {
                    break; // Queue is empty
                }
            }
            
            if (_currentEventCount > MaxStoredEvents)
            {
                System.Diagnostics.Debug.WriteLine($"Pruned events. Now storing: {_currentEventCount}");
            }
        }
    }

    private void OnCaptureError(object? sender, string error)
    {
        System.Diagnostics.Debug.WriteLine($"Capture error: {error}");
        ErrorOccurred?.Invoke(this, error);
    }

    private void OnPatternDetected(object? sender, DetectedPattern pattern)
    {
        _patterns.Add(pattern);
        PatternDetected?.Invoke(this, pattern);
        System.Diagnostics.Debug.WriteLine($"Pattern detected: {pattern.PatternType} - {pattern.Description}");
    }

    public MonitorStatistics GetStatistics()
    {
        var uptime = IsMonitoring ? DateTime.Now - _startTime : TimeSpan.Zero;
        var eventsPerSecond = uptime.TotalSeconds > 0 ? _totalEvents / uptime.TotalSeconds : 0;

        return new MonitorStatistics
        {
            TotalEvents = _totalEvents,
            EventsPerSecond = eventsPerSecond,
            TotalPatterns = _patterns.Count,
            Uptime = uptime,
            StoredEvents = _currentEventCount
        };
    }
    
    public List<DetectedPattern> GetRecentPatterns(int count = 10)
    {
        return _patterns.TakeLast(count).ToList();
    }
    
    // OPTIMIZED: Return events as list efficiently
    public List<SystemEvent> GetCapturedEvents()
    {
        return _capturedEvents.ToList();
    }
    
    // OPTIMIZED: Get only the most recent N events (much faster!)
    public List<SystemEvent> GetCapturedEvents(int count)
    {
        // Take from the end of the queue (most recent)
        return _capturedEvents.Reverse().Take(count).Reverse().ToList();
    }
    
    // NEW: Clear all captured events
    public void ClearCapturedEvents()
    {
        while (_capturedEvents.TryDequeue(out _)) { }
        _currentEventCount = 0;
        System.Diagnostics.Debug.WriteLine("All captured events cleared");
    }
}

public class MonitorStatistics
{
    public int TotalEvents { get; set; }
    public double EventsPerSecond { get; set; }
    public int TotalPatterns { get; set; }
    public TimeSpan Uptime { get; set; }
    public int StoredEvents { get; set; } // NEW: Track how many are actually in memory
}
