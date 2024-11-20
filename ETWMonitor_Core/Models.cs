using System;
using System.Collections.Generic;

namespace EtwMonitor.Core.Models
{
    public enum EventType
    {
        FileSystem,
        Registry,
        Process,
        Network,
        Unknown
    }

    public enum Severity
    {
        Info,
        Low,
        Medium,
        High,
        Critical
    }

    public class SystemEvent
    {
        public long Id { get; set; }
        public DateTime Timestamp { get; set; }
        public EventType Type { get; set; }
        public string? ProcessName { get; set; }
        public int ProcessId { get; set; }
        public int ThreadId { get; set; }
        public string? Operation { get; set; }
        public string? Path { get; set; }
        public string? Result { get; set; }
        public string? Details { get; set; }
        public int? ErrorCode { get; set; }
        public long? Duration { get; set; } // microseconds
        
        // Additional context
        public string? UserName { get; set; }
        public string? SessionId { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    public class DetectedPattern
    {
        public int Id { get; set; }
        public string PatternType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Severity Severity { get; set; }
        public double Confidence { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public int OccurrenceCount { get; set; }
        public string Suggestion { get; set; } = string.Empty;
        public List<long> RelatedEventIds { get; set; } = new();
        
        // AI Analysis
        public string? CopilotAnalysis { get; set; }
        public string? RootCause { get; set; }
        public string? Remediation { get; set; }
        public DateTime? AnalyzedAt { get; set; }
    }

    public class Diagnosis
    {
        public int Id { get; set; }
        public int PatternId { get; set; }
        public DateTime Timestamp { get; set; }
        public string RootCause { get; set; } = string.Empty;
        public string Remediation { get; set; } = string.Empty;
        public List<string> PreventionMeasures { get; set; } = new();
        public double CopilotConfidence { get; set; }
        public string? AdditionalContext { get; set; }
        public bool Resolved { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }

    public class PatternStatistics
    {
        public string PatternType { get; set; } = string.Empty;
        public int TotalOccurrences { get; set; }
        public DateTime FirstOccurrence { get; set; }
        public DateTime LastOccurrence { get; set; }
        public double AverageConfidence { get; set; }
        public Dictionary<Severity, int> BySeverity { get; set; } = new();
    }

    public class TicketInfo
    {
        public string TicketId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime ReportedAt { get; set; }
        public string ReportedBy { get; set; } = string.Empty;
        public List<string> Symptoms { get; set; } = new();
    }
}
