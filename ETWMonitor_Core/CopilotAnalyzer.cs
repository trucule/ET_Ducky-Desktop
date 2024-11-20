using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using EtwMonitor.Core.Models;
using Serilog;

namespace EtwMonitor.Core.AI
{
    public class CopilotAnalyzer
    {
        private readonly ILogger _logger;
        private readonly AzureOpenAIClient _azureClient;
        private readonly ChatClient _chatClient;
        private readonly int _maxTokens;

        public CopilotAnalyzer(ILogger logger, string endpoint, string apiKey, string deploymentName = "gpt-4", int maxTokens = 2000)
        {
            _logger = logger;
            _maxTokens = maxTokens;
            
            _azureClient = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));
            _chatClient = _azureClient.GetChatClient(deploymentName);
            
            _logger.Information("Copilot Analyzer initialized with deployment: {Deployment}", deploymentName);
        }

        public async Task<Diagnosis> AnalyzePatternAsync(DetectedPattern pattern, List<SystemEvent> relatedEvents)
        {
            try
            {
                _logger.Information("Analyzing pattern: {Type} - {Description}", pattern.PatternType, pattern.Description);
                
                var prompt = BuildAnalysisPrompt(pattern, relatedEvents);
                
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(GetSystemPrompt()),
                    new UserChatMessage(prompt)
                };

                var options = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = _maxTokens,
                    Temperature = 0.7f,
                    FrequencyPenalty = 0.0f,
                    PresencePenalty = 0.0f
                };

                var response = await _chatClient.CompleteChatAsync(messages, options);
                var analysisText = response.Value.Content[0].Text;
                
                var diagnosis = ParseDiagnosis(analysisText, pattern);
                
                _logger.Information("Pattern analysis complete. Confidence: {Confidence:P0}", diagnosis.CopilotConfidence);
                
                return diagnosis;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error analyzing pattern with Copilot");
                
                return new Diagnosis
                {
                    PatternId = pattern.Id,
                    Timestamp = DateTime.Now,
                    RootCause = "Analysis failed",
                    Remediation = "Unable to analyze pattern due to error: " + ex.Message,
                    CopilotConfidence = 0.0
                };
            }
        }

        private string GetSystemPrompt()
        {
            return @"You are an expert system administrator and Windows internals specialist helping diagnose issues from ETW (Event Tracing for Windows) event patterns.

Your role is to:
1. Analyze event patterns and identify root causes
2. Provide clear, actionable remediation steps
3. Suggest preventive measures
4. Be concise but thorough

Format your response as:

ROOT CAUSE:
[Clear explanation of what's causing the issue]

REMEDIATION:
[Step-by-step fix, numbered]
1. First step
2. Second step
...

PREVENTION:
[How to prevent this in the future]
- Measure 1
- Measure 2
...

CONFIDENCE: [0.0-1.0]";
        }

        private string BuildAnalysisPrompt(DetectedPattern pattern, List<SystemEvent> relatedEvents)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("Analyze the following system event pattern:");
            sb.AppendLine();
            sb.AppendLine($"PATTERN TYPE: {pattern.PatternType}");
            sb.AppendLine($"DESCRIPTION: {pattern.Description}");
            sb.AppendLine($"SEVERITY: {pattern.Severity}");
            sb.AppendLine($"OCCURRENCES: {pattern.OccurrenceCount}");
            sb.AppendLine($"TIME SPAN: {pattern.FirstSeen:HH:mm:ss} - {pattern.LastSeen:HH:mm:ss}");
            sb.AppendLine();
            
            if (relatedEvents.Any())
            {
                sb.AppendLine("RELATED EVENTS:");
                
                // Group events for clarity
                var eventGroups = relatedEvents
                    .GroupBy(e => new { e.ProcessName, e.Operation, e.Result })
                    .OrderByDescending(g => g.Count())
                    .Take(10); // Top 10 patterns
                
                foreach (var group in eventGroups)
                {
                    var sample = group.First();
                    sb.AppendLine($"- {group.Key.ProcessName} | {group.Key.Operation} | {group.Key.Result}");
                    sb.AppendLine($"  Path: {sample.Path ?? "N/A"}");
                    sb.AppendLine($"  Count: {group.Count()}");
                    
                    if (sample.ErrorCode.HasValue)
                    {
                        sb.AppendLine($"  Error Code: 0x{sample.ErrorCode:X}");
                    }
                }
                
                sb.AppendLine();
            }
            
            // Add context about the system
            sb.AppendLine("CONTEXT:");
            sb.AppendLine($"- Operating System: Windows");
            sb.AppendLine($"- Pattern detected by: ETW Monitor");
            sb.AppendLine($"- Detection confidence: {pattern.Confidence:P0}");
            
            if (!string.IsNullOrEmpty(pattern.Suggestion))
            {
                sb.AppendLine($"- Initial suggestion: {pattern.Suggestion}");
            }
            
            sb.AppendLine();
            sb.AppendLine("Provide a thorough analysis with root cause, remediation steps, and prevention measures.");
            
            return sb.ToString();
        }

        private Diagnosis ParseDiagnosis(string analysisText, DetectedPattern pattern)
        {
            var diagnosis = new Diagnosis
            {
                PatternId = pattern.Id,
                Timestamp = DateTime.Now,
                Resolved = false
            };
            
            try
            {
                // Extract sections from the response
                var rootCause = ExtractSection(analysisText, "ROOT CAUSE:", "REMEDIATION:");
                var remediation = ExtractSection(analysisText, "REMEDIATION:", "PREVENTION:");
                var prevention = ExtractSection(analysisText, "PREVENTION:", "CONFIDENCE:");
                var confidenceStr = ExtractSection(analysisText, "CONFIDENCE:", null);
                
                diagnosis.RootCause = rootCause?.Trim() ?? "Unable to determine root cause";
                diagnosis.Remediation = remediation?.Trim() ?? "No remediation steps provided";
                
                // Parse prevention measures
                if (!string.IsNullOrEmpty(prevention))
                {
                    diagnosis.PreventionMeasures = prevention
                        .Split('\n')
                        .Where(line => line.Trim().StartsWith("-") || line.Trim().StartsWith("•"))
                        .Select(line => line.Trim().TrimStart('-', '•').Trim())
                        .Where(line => !string.IsNullOrEmpty(line))
                        .ToList();
                }
                
                // Parse confidence
                if (!string.IsNullOrEmpty(confidenceStr) && 
                    double.TryParse(confidenceStr.Trim(), out var confidence))
                {
                    diagnosis.CopilotConfidence = Math.Clamp(confidence, 0.0, 1.0);
                }
                else
                {
                    diagnosis.CopilotConfidence = 0.75; // Default confidence
                }
                
                diagnosis.AdditionalContext = analysisText; // Store full response
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error parsing diagnosis from Copilot response");
                diagnosis.RootCause = "Parsing error";
                diagnosis.Remediation = analysisText; // Return raw text
                diagnosis.CopilotConfidence = 0.5;
            }
            
            return diagnosis;
        }

        private string? ExtractSection(string text, string startMarker, string? endMarker)
        {
            try
            {
                var startIndex = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
                if (startIndex == -1) return null;
                
                startIndex += startMarker.Length;
                
                int endIndex;
                if (endMarker != null)
                {
                    endIndex = text.IndexOf(endMarker, startIndex, StringComparison.OrdinalIgnoreCase);
                    if (endIndex == -1) endIndex = text.Length;
                }
                else
                {
                    endIndex = text.Length;
                }
                
                return text.Substring(startIndex, endIndex - startIndex);
            }
            catch
            {
                return null;
            }
        }

        public async Task<string> ChatAsync(string userMessage, List<string>? conversationHistory = null)
        {
            try
            {
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage("You are a helpful assistant specialized in Windows system administration and troubleshooting.")
                };
                
                // Add conversation history if provided
                if (conversationHistory != null)
                {
                    foreach (var msg in conversationHistory)
                    {
                        messages.Add(new UserChatMessage(msg));
                    }
                }
                
                messages.Add(new UserChatMessage(userMessage));
                
                var options = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = 1000,
                    Temperature = 0.8f
                };
                
                var response = await _chatClient.CompleteChatAsync(messages, options);
                return response.Value.Content[0].Text;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in chat interaction");
                return "I encountered an error processing your request. Please try again.";
            }
        }
    }
}
