using System;
using System.Collections.Generic;
using System.Linq;
using EtwMonitor.Core.Models;
using Serilog;

namespace EtwMonitor.Core.Patterns
{
    public class AdvancedPatternDetector
    {
        private readonly ILogger _logger;
        private readonly Dictionary<string, List<SystemEvent>> _eventBuckets = new();
        private readonly List<SystemEvent> _recentEvents = new();
        private readonly List<DetectedPattern> _activePatterns = new();
        private readonly object _lock = new();
        
        private const int WINDOW_SECONDS = 30;
        private const int MAX_RECENT_EVENTS = 10000;
        private const int ERROR_BURST_THRESHOLD = 5;
        private const int REGISTRY_THRASH_THRESHOLD = 50;
        private const int FILE_LOCK_THRESHOLD = 3;

        public AdvancedPatternDetector(ILogger logger)
        {
            _logger = logger;
        }

        public List<DetectedPattern> AnalyzeEvent(SystemEvent evt)
        {
            lock (_lock)
            {
                var patterns = new List<DetectedPattern>();
                
                // Add to recent events
                _recentEvents.Add(evt);
                if (_recentEvents.Count > MAX_RECENT_EVENTS)
                {
                    _recentEvents.RemoveAt(0);
                }
                
                // Clean old events from buckets
                CleanOldEvents();
                
                // Run pattern detection algorithms
                patterns.AddRange(DetectErrorBurst(evt));
                patterns.AddRange(DetectAccessDenied(evt));
                patterns.AddRange(DetectFileLockConflicts(evt));
                patterns.AddRange(DetectRegistryThrashing(evt));
                patterns.AddRange(DetectRapidProcessCrashes(evt));
                patterns.AddRange(DetectCascadingFailures(evt));
                patterns.AddRange(DetectPerformanceDegradation(evt));
                
                // Update active patterns
                foreach (var pattern in patterns)
                {
                    _activePatterns.Add(pattern);
                    _logger.Information("Pattern detected: {Type} - {Description}", 
                        pattern.PatternType, pattern.Description);
                }
                
                return patterns;
            }
        }

        #region Pattern Detection Algorithms

        private List<DetectedPattern> DetectErrorBurst(SystemEvent evt)
        {
            var patterns = new List<DetectedPattern>();
            
            if (evt.Result != "SUCCESS" && !string.IsNullOrEmpty(evt.Result))
            {
                var key = $"error_{evt.Type}_{evt.Operation}_{evt.Result}";
                
                if (!_eventBuckets.ContainsKey(key))
                    _eventBuckets[key] = new List<SystemEvent>();
                
                _eventBuckets[key].Add(evt);
                
                if (_eventBuckets[key].Count >= ERROR_BURST_THRESHOLD)
                {
                    patterns.Add(new DetectedPattern
                    {
                        PatternType = "ErrorBurst",
                        Description = $"Error burst detected: {_eventBuckets[key].Count} {evt.Operation} failures in {WINDOW_SECONDS}s",
                        Severity = Severity.High,
                        Confidence = 0.9,
                        FirstSeen = _eventBuckets[key].First().Timestamp,
                        LastSeen = evt.Timestamp,
                        OccurrenceCount = _eventBuckets[key].Count,
                        Suggestion = $"Check {evt.Path ?? evt.ProcessName} for permissions, resource availability, or configuration issues",
                        RelatedEventIds = _eventBuckets[key].Select(e => e.Id).ToList()
                    });
                    
                    _eventBuckets[key].Clear();
                }
            }
            
            return patterns;
        }

        private List<DetectedPattern> DetectAccessDenied(SystemEvent evt)
        {
            var patterns = new List<DetectedPattern>();
            
            if (evt.Result?.Contains("DENIED") == true || 
                evt.Result?.Contains("ACCESS") == true ||
                evt.Result?.Contains("PERMISSION") == true)
            {
                patterns.Add(new DetectedPattern
                {
                    PatternType = "AccessDenied",
                    Description = $"Access denied: {evt.Operation} on {evt.Path}",
                    Severity = Severity.Medium,
                    Confidence = 1.0,
                    FirstSeen = evt.Timestamp,
                    LastSeen = evt.Timestamp,
                    OccurrenceCount = 1,
                    Suggestion = "Verify user permissions, ACLs, and security policies. Check if running with appropriate privileges.",
                    RelatedEventIds = new List<long> { evt.Id }
                });
            }
            
            return patterns;
        }

        private List<DetectedPattern> DetectFileLockConflicts(SystemEvent evt)
        {
            var patterns = new List<DetectedPattern>();
            
            if (evt.Type == EventType.FileSystem && 
                (evt.Result?.Contains("SHARING") == true || evt.Result?.Contains("LOCK") == true))
            {
                var key = $"filelock_{evt.Path}";
                
                if (!_eventBuckets.ContainsKey(key))
                    _eventBuckets[key] = new List<SystemEvent>();
                
                _eventBuckets[key].Add(evt);
                
                if (_eventBuckets[key].Count >= FILE_LOCK_THRESHOLD)
                {
                    var processes = _eventBuckets[key].Select(e => e.ProcessName).Distinct().ToList();
                    
                    patterns.Add(new DetectedPattern
                    {
                        PatternType = "FileLockConflict",
                        Description = $"File lock conflict: {processes.Count} processes competing for {evt.Path}",
                        Severity = Severity.High,
                        Confidence = 0.85,
                        FirstSeen = _eventBuckets[key].First().Timestamp,
                        LastSeen = evt.Timestamp,
                        OccurrenceCount = _eventBuckets[key].Count,
                        Suggestion = $"Processes in conflict: {string.Join(", ", processes)}. Review file access patterns or implement file locking strategy.",
                        RelatedEventIds = _eventBuckets[key].Select(e => e.Id).ToList()
                    });
                    
                    _eventBuckets[key].Clear();
                }
            }
            
            return patterns;
        }

        private List<DetectedPattern> DetectRegistryThrashing(SystemEvent evt)
        {
            var patterns = new List<DetectedPattern>();
            
            if (evt.Type == EventType.Registry)
            {
                var key = $"regthrash_{evt.Path}";
                
                if (!_eventBuckets.ContainsKey(key))
                    _eventBuckets[key] = new List<SystemEvent>();
                
                _eventBuckets[key].Add(evt);
                
                if (_eventBuckets[key].Count >= REGISTRY_THRASH_THRESHOLD)
                {
                    patterns.Add(new DetectedPattern
                    {
                        PatternType = "RegistryThrashing",
                        Description = $"Registry thrashing: {_eventBuckets[key].Count} operations on {evt.Path} in {WINDOW_SECONDS}s",
                        Severity = Severity.Medium,
                        Confidence = 0.8,
                        FirstSeen = _eventBuckets[key].First().Timestamp,
                        LastSeen = evt.Timestamp,
                        OccurrenceCount = _eventBuckets[key].Count,
                        Suggestion = "Application may be misconfigured or in a polling loop. Check registry key purpose and optimize access patterns.",
                        RelatedEventIds = _eventBuckets[key].Select(e => e.Id).ToList()
                    });
                    
                    _eventBuckets[key].Clear();
                }
            }
            
            return patterns;
        }

        private List<DetectedPattern> DetectRapidProcessCrashes(SystemEvent evt)
        {
            var patterns = new List<DetectedPattern>();
            
            if (evt.Type == EventType.Process && evt.Operation == "Stop")
            {
                var key = $"crash_{evt.ProcessName}";
                
                if (!_eventBuckets.ContainsKey(key))
                    _eventBuckets[key] = new List<SystemEvent>();
                
                _eventBuckets[key].Add(evt);
                
                if (_eventBuckets[key].Count >= 3) // 3 crashes in window
                {
                    patterns.Add(new DetectedPattern
                    {
                        PatternType = "RapidProcessCrash",
                        Description = $"Process crash loop: {evt.ProcessName} terminated {_eventBuckets[key].Count} times in {WINDOW_SECONDS}s",
                        Severity = Severity.Critical,
                        Confidence = 0.95,
                        FirstSeen = _eventBuckets[key].First().Timestamp,
                        LastSeen = evt.Timestamp,
                        OccurrenceCount = _eventBuckets[key].Count,
                        Suggestion = "Application is crash-looping. Check event logs, examine crash dumps, and review recent changes.",
                        RelatedEventIds = _eventBuckets[key].Select(e => e.Id).ToList()
                    });
                    
                    _eventBuckets[key].Clear();
                }
            }
            
            return patterns;
        }

        private List<DetectedPattern> DetectCascadingFailures(SystemEvent evt)
        {
            var patterns = new List<DetectedPattern>();
            
            if (evt.Result != "SUCCESS" && !string.IsNullOrEmpty(evt.Result))
            {
                // Look for errors from different processes in quick succession
                var recentErrors = _recentEvents
                    .Where(e => e.Timestamp >= evt.Timestamp.AddSeconds(-5))
                    .Where(e => e.Result != "SUCCESS" && !string.IsNullOrEmpty(e.Result))
                    .GroupBy(e => e.ProcessName)
                    .Where(g => g.Count() >= 2)
                    .ToList();
                
                if (recentErrors.Count >= 2) // At least 2 different processes with errors
                {
                    var processNames = recentErrors.Select(g => g.Key).ToList();
                    
                    patterns.Add(new DetectedPattern
                    {
                        PatternType = "CascadingFailure",
                        Description = $"Cascading failure detected: {processNames.Count} processes experiencing errors simultaneously",
                        Severity = Severity.Critical,
                        Confidence = 0.75,
                        FirstSeen = recentErrors.SelectMany(g => g).Min(e => e.Timestamp),
                        LastSeen = evt.Timestamp,
                        OccurrenceCount = recentErrors.Sum(g => g.Count()),
                        Suggestion = $"Multiple processes affected: {string.Join(", ", processNames)}. Check system resources, network connectivity, or shared dependencies.",
                        RelatedEventIds = recentErrors.SelectMany(g => g).Select(e => e.Id).ToList()
                    });
                }
            }
            
            return patterns;
        }

        private List<DetectedPattern> DetectPerformanceDegradation(SystemEvent evt)
        {
            var patterns = new List<DetectedPattern>();
            
            // Check if operations are taking unusually long
            if (evt.Duration.HasValue && evt.Duration.Value > 1000000) // > 1 second
            {
                var key = $"perf_{evt.Type}_{evt.Operation}";
                
                if (!_eventBuckets.ContainsKey(key))
                    _eventBuckets[key] = new List<SystemEvent>();
                
                _eventBuckets[key].Add(evt);
                
                if (_eventBuckets[key].Count >= 5)
                {
                    var avgDuration = _eventBuckets[key].Average(e => e.Duration ?? 0);
                    
                    patterns.Add(new DetectedPattern
                    {
                        PatternType = "PerformanceDegradation",
                        Description = $"Performance degradation: {evt.Operation} operations averaging {avgDuration / 1000}ms",
                        Severity = Severity.Medium,
                        Confidence = 0.7,
                        FirstSeen = _eventBuckets[key].First().Timestamp,
                        LastSeen = evt.Timestamp,
                        OccurrenceCount = _eventBuckets[key].Count,
                        Suggestion = "Check system resources (CPU, memory, disk I/O), network latency, or database performance.",
                        RelatedEventIds = _eventBuckets[key].Select(e => e.Id).ToList()
                    });
                    
                    _eventBuckets[key].Clear();
                }
            }
            
            return patterns;
        }

        #endregion

        private void CleanOldEvents()
        {
            var cutoff = DateTime.Now.AddSeconds(-WINDOW_SECONDS);
            
            foreach (var key in _eventBuckets.Keys.ToList())
            {
                _eventBuckets[key].RemoveAll(e => e.Timestamp < cutoff);
                
                if (_eventBuckets[key].Count == 0)
                {
                    _eventBuckets.Remove(key);
                }
            }
            
            _recentEvents.RemoveAll(e => e.Timestamp < cutoff.AddSeconds(-WINDOW_SECONDS));
        }

        public List<DetectedPattern> GetActivePatterns()
        {
            lock (_lock)
            {
                return _activePatterns.ToList();
            }
        }

        public PatternStatistics GetStatistics()
        {
            lock (_lock)
            {
                return new PatternStatistics
                {
                    TotalOccurrences = _activePatterns.Count,
                    FirstOccurrence = _activePatterns.Any() ? _activePatterns.Min(p => p.FirstSeen) : DateTime.MinValue,
                    LastOccurrence = _activePatterns.Any() ? _activePatterns.Max(p => p.LastSeen) : DateTime.MinValue,
                    AverageConfidence = _activePatterns.Any() ? _activePatterns.Average(p => p.Confidence) : 0,
                    BySeverity = _activePatterns
                        .GroupBy(p => p.Severity)
                        .ToDictionary(g => g.Key, g => g.Count())
                };
            }
        }
    }
}
