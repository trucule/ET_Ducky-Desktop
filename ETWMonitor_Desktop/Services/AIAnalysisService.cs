using EtwMonitor.Core.Models;
using EtwMonitor.Desktop.Services;
using System.Collections.Concurrent;

namespace EtwMonitor.Desktop.Services;

public class AIAnalysisService : IDisposable
{
    private readonly AIProviderService _aiService;
    private readonly MonitorStateService _monitorState;
    private System.Threading.Timer? _analysisTimer;
    private readonly ConcurrentQueue<AIInsight> _insights = new();
    private DateTime _lastAnalysisTime = DateTime.MinValue;
    private const int MaxInsights = 20;
    private bool _isAnalyzing = false;
    private bool _isEnabled = false;

    // Analysis settings
    private int _analysisIntervalSeconds = 30; // Analyze every 30 seconds
    private int _eventsToAnalyze = 100; // Look at last 100 events

    public event EventHandler<AIInsight>? InsightGenerated;
    public bool IsEnabled => _isEnabled;
    public bool IsAnalyzing => _isAnalyzing;
    public IReadOnlyList<AIInsight> RecentInsights => _insights.ToList().AsReadOnly();

    private readonly DiagnosticsService _diagnosticsService; // add this field near top

    public AIAnalysisService(
        AIProviderService aiService,
        MonitorStateService monitorState,
        DiagnosticsService diagnosticsService)
    {
        _aiService = aiService;
        _monitorState = monitorState;
        _diagnosticsService = diagnosticsService;
    }

    public async Task<AIInsight?> AnalyzeCurrentEventsAsync()
    {
        if (_isAnalyzing)
            return null;

        if (_aiService.CurrentProvider == null || !_aiService.CurrentProvider.IsConfigured)
            return null;

        try
        {
            _isAnalyzing = true;

            var events = _monitorState.GetCapturedEvents(_eventsToAnalyze);
            if (events.Count < 10)
                return null;

            // Analyze all available events
            var errorEvents = events.Where(e => 
                e.Result?.Contains("ERROR", StringComparison.OrdinalIgnoreCase) == true ||
                e.Result?.Contains("FAIL", StringComparison.OrdinalIgnoreCase) == true).ToList();

            var highActivityProcesses = events
                .GroupBy(e => e.ProcessName)
                .Where(g => g.Count() > 10)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .ToList();

            var suspiciousPatterns = DetectSuspiciousPatterns(events);

            // Build analysis prompt
            var prompt = BuildAnalysisPrompt(events, errorEvents, highActivityProcesses, suspiciousPatterns);

            if (string.IsNullOrEmpty(prompt))
                return null;

            // Get AI analysis
            var analysis = await _aiService.ChatAsync(prompt);

            // Create insight
            var insight = new AIInsight
            {
                Timestamp = DateTime.Now,
                Title = DetermineInsightTitle(errorEvents, highActivityProcesses, suspiciousPatterns),
                Analysis = analysis,
                EventCount = events.Count,
                Severity = DetermineSeverity(errorEvents, suspiciousPatterns),
                RelatedEvents = events.Take(10).ToList()
            };

            // Add to queue
            _insights.Enqueue(insight);
            while (_insights.Count > MaxInsights)
            {
                _insights.TryDequeue(out _);
            }

            // Notify listeners
            InsightGenerated?.Invoke(this, insight);

            return insight;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in manual AI analysis: {ex.Message}");
            return null;
        }
        finally
        {
            _isAnalyzing = false;
        }
    }

    public void Start()
    {
        if (_isEnabled) return;

        _isEnabled = true;
        _analysisTimer = new System.Threading.Timer(async _ => await AnalyzeEventsAsync(), 
            null, 
            TimeSpan.FromSeconds(5), // Start after 5 seconds
            TimeSpan.FromSeconds(_analysisIntervalSeconds));
    }

    public void Stop()
    {
        _isEnabled = false;
        _analysisTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void UpdateSettings(int intervalSeconds, int eventsToAnalyze)
    {
        _analysisIntervalSeconds = intervalSeconds;
        _eventsToAnalyze = eventsToAnalyze;

        if (_isEnabled && _analysisTimer != null)
        {
            _analysisTimer.Change(
                TimeSpan.FromSeconds(intervalSeconds),
                TimeSpan.FromSeconds(intervalSeconds));
        }
    }

    private async Task AnalyzeEventsAsync()
    {
        if (_isAnalyzing || !_isEnabled || !_monitorState.IsMonitoring)
            return;

        if (_aiService.CurrentProvider == null || !_aiService.CurrentProvider.IsConfigured)
            return;

        try
        {
            _isAnalyzing = true;

            var events = _monitorState.GetCapturedEvents(_eventsToAnalyze);
            if (events.Count < 10) // Need at least 10 events to analyze
                return;

            // Only analyze events since last analysis
            var newEvents = events.Where(e => e.Timestamp > _lastAnalysisTime).ToList();
            if (newEvents.Count < 5)
                return;

            _lastAnalysisTime = DateTime.Now;

            // Group events to find interesting patterns
            var errorEvents = newEvents.Where(e => 
                e.Result?.Contains("ERROR", StringComparison.OrdinalIgnoreCase) == true ||
                e.Result?.Contains("FAIL", StringComparison.OrdinalIgnoreCase) == true).ToList();

            var highActivityProcesses = newEvents
                .GroupBy(e => e.ProcessName)
                .Where(g => g.Count() > 10)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .ToList();

            var suspiciousPatterns = DetectSuspiciousPatterns(newEvents);

            // Build analysis prompt
            var prompt = BuildAnalysisPrompt(newEvents, errorEvents, highActivityProcesses, suspiciousPatterns);

            if (string.IsNullOrEmpty(prompt))
                return;

            // Get AI analysis
            var analysis = await _aiService.ChatAsync(prompt);

            // Create insight
            var insight = new AIInsight
            {
                Timestamp = DateTime.Now,
                Title = DetermineInsightTitle(errorEvents, highActivityProcesses, suspiciousPatterns),
                Analysis = analysis,
                EventCount = newEvents.Count,
                Severity = DetermineSeverity(errorEvents, suspiciousPatterns),
                RelatedEvents = newEvents.Take(10).ToList()
            };

            // Add to queue (keep only recent insights)
            _insights.Enqueue(insight);
            while (_insights.Count > MaxInsights)
            {
                _insights.TryDequeue(out _);
            }

            // Notify listeners
            InsightGenerated?.Invoke(this, insight);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in AI analysis: {ex.Message}");
        }
        finally
        {
            _isAnalyzing = false;
        }
    }

    private string BuildAnalysisPrompt(
        List<SystemEvent> allEvents,
        List<SystemEvent> errorEvents,
        List<IGrouping<string?, SystemEvent>> highActivityProcesses,
        List<string> suspiciousPatterns)
    {
        if (allEvents.Count == 0)
            return string.Empty;

        var prompt = $@"You are analyzing Windows ETW events in real-time. Provide a brief, actionable analysis.

**Recent Activity Summary:**
- {allEvents.Count} events captured in the last {_analysisIntervalSeconds} seconds
- {errorEvents.Count} error events detected
- Event types: {string.Join(", ", allEvents.GroupBy(e => e.Type).Select(g => $"{g.Key}({g.Count()})"))}
";

        if (errorEvents.Any())
        {
            prompt += $@"
**Errors Detected:**
{string.Join("\n", errorEvents.Take(5).Select(e => $"- [{e.ProcessName}] {e.Operation}: {e.Path ?? e.Details} - {e.Result}"))}
";
        }

        if (highActivityProcesses.Any())
        {
            prompt += $@"
**High Activity Processes:**
{string.Join("\n", highActivityProcesses.Select(g => $"- {g.Key}: {g.Count()} events"))}
";
        }

        if (suspiciousPatterns.Any())
        {
            prompt += $@"
**Suspicious Patterns:**
{string.Join("\n", suspiciousPatterns.Select(p => $"- {p}"))}
";
        }

        prompt += @"

**Provide:**
1. What's happening (1-2 sentences)
2. Any potential issues or concerns
3. Recommended action (if any)

Keep response under 150 words and focus on actionable insights.";

        return prompt;
    }

    private List<string> DetectSuspiciousPatterns(List<SystemEvent> events)
    {
        var patterns = new List<string>();

        // Check for repeated failures on same path
        var repeatedFailures = events
            .Where(e => !string.IsNullOrEmpty(e.Result) && 
                       (e.Result.Contains("ERROR") || e.Result.Contains("FAIL")))
            .GroupBy(e => e.Path)
            .Where(g => g.Count() > 3)
            .Select(g => g.Key)
            .ToList();

        if (repeatedFailures.Any())
        {
            patterns.Add($"Repeated failures accessing: {string.Join(", ", repeatedFailures.Take(2))}");
        }

        // Check for unusual network activity
        var networkEvents = events.Where(e => e.Type == EventType.Network).ToList();
        if (networkEvents.Count > 50)
        {
            patterns.Add($"High network activity: {networkEvents.Count} network events");
        }

        // Check for registry modifications
        var registryWrites = events
            .Where(e => e.Type == EventType.Registry && 
                       e.Operation?.Contains("Write", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        
        if (registryWrites.Count > 20)
        {
            patterns.Add($"High registry write activity: {registryWrites.Count} registry modifications");
        }

        return patterns;
    }

    private string DetermineInsightTitle(
        List<SystemEvent> errorEvents,
        List<IGrouping<string?, SystemEvent>> highActivityProcesses,
        List<string> suspiciousPatterns)
    {
        if (errorEvents.Count > 10)
            return $"⚠️ Multiple Errors Detected ({errorEvents.Count} errors)";
        
        if (suspiciousPatterns.Any())
            return $"🔍 Suspicious Activity Detected";
        
        if (highActivityProcesses.Any())
            return $"📊 High Process Activity: {highActivityProcesses.First().Key}";
        
        return "✅ System Activity Normal";
    }

    private InsightSeverity DetermineSeverity(
        List<SystemEvent> errorEvents,
        List<string> suspiciousPatterns)
    {
        if (errorEvents.Count > 10 || suspiciousPatterns.Count > 2)
            return InsightSeverity.High;
        
        if (errorEvents.Count > 3 || suspiciousPatterns.Count > 0)
            return InsightSeverity.Medium;
        
        return InsightSeverity.Low;
    }

    public async Task<AIInsight?> AnalyzeDiagnosticResultAsync(
    string userRequest,
    DiagnosticResult diagnosticResult)
    {
        if (_aiService.CurrentProvider == null || !_aiService.CurrentProvider.IsConfigured)
            return null;

        try
        {
            var events = _monitorState.GetCapturedEvents(_eventsToAnalyze);

            var recentEventsSummary = string.Join(Environment.NewLine,
                events
                    .OrderByDescending(e => e.Timestamp)
                    .Take(25)
                    .Select(e =>
                        $"[{e.Timestamp:HH:mm:ss}] {e.Type} | {e.ProcessName} | {e.Operation} | {e.Path ?? e.Details} | {e.Result}"));

            var prompt = $@"
You are helping troubleshoot a Windows system using ETW data and explicit diagnostic checks.

The user requested:
""{userRequest}""

A diagnostic action was performed:
- Action type: {diagnosticResult.ActionType}
- Success: {diagnosticResult.Success}
- Summary: {diagnosticResult.Summary}
- Parameter: {diagnosticResult.Parameter}

Details:
{diagnosticResult.Details}

Here are up to {_eventsToAnalyze} recent ETW events (showing at most 25):
{recentEventsSummary}

Based on the ETW events and the diagnostic result:
1. Explain what might be happening (1–3 sentences).
2. Call out any obvious problems or risks.
3. Recommend the next 1–3 concrete troubleshooting steps.

Keep response under 200 words and focus on actionable guidance.
";

            var analysis = await _aiService.ChatAsync(prompt);

            var insight = new AIInsight
            {
                Timestamp = DateTime.Now,
                Title = $"🧪 Diagnostic: {diagnosticResult.ActionType}",
                Analysis = analysis,
                EventCount = events.Count,
                Severity = InsightSeverity.Medium,
                RelatedEvents = events.Take(10).ToList()
            };

            _insights.Enqueue(insight);
            while (_insights.Count > MaxInsights)
            {
                _insights.TryDequeue(out _);
            }

            InsightGenerated?.Invoke(this, insight);
            return insight;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in diagnostic AI analysis: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        Stop();
        _analysisTimer?.Dispose();
    }
}

public class AIInsight
{
    public DateTime Timestamp { get; set; }
    public string Title { get; set; } = "";
    public string Analysis { get; set; } = "";
    public int EventCount { get; set; }
    public InsightSeverity Severity { get; set; }
    public List<SystemEvent> RelatedEvents { get; set; } = new();

    // For future use: let the AI suggest an on-demand diagnostic the user can run
    public DiagnosticActionType? SuggestedActionType { get; set; }
    public string? SuggestedActionParameter { get; set; }
}

public enum InsightSeverity
{
    Low,
    Medium,
    High
}
