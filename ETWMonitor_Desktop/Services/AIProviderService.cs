using EtwMonitor.Core.Models;
using System.Text.Json;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Net.Http.Json;

namespace EtwMonitor.Desktop.Services;

public interface IAIProvider
{
    string Id { get; }
    string Name { get; }
    bool IsConfigured { get; }
    Task<string> AnalyzePatternAsync(DetectedPattern pattern, List<SystemEvent> events);
    Task<string> ChatAsync(string message, List<string>? history = null);
}

public class CopilotProvider : IAIProvider
{
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxTokens;

    public string Id => "copilot";
    public string Name => "Copilot (Azure OpenAI)";
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_endpoint);

    public CopilotProvider(string endpoint, string apiKey, string model, int maxTokens)
    {
        _endpoint = endpoint ?? "";
        _apiKey = apiKey ?? "";
        _model = model ?? "gpt-4";
        _maxTokens = maxTokens > 0 ? maxTokens : 2000;
    }

    public async Task<string> AnalyzePatternAsync(DetectedPattern pattern, List<SystemEvent> events)
    {
        if (!IsConfigured)
            return "Copilot provider is not properly configured. Please check your API key and endpoint in Settings.";
            
        try
        {
            var azureClient = new AzureOpenAIClient(new Uri(_endpoint), new ApiKeyCredential(_apiKey));
            var chatClient = azureClient.GetChatClient(_model);
            
            var prompt = $@"Analyze this system pattern:
Type: {pattern.PatternType}
Severity: {pattern.Severity}
Description: {pattern.Description}
Confidence: {pattern.Confidence:P0}

Recent events:
{string.Join("\n", events.Take(10).Select(e => $"- {e.Operation}: {e.Path ?? e.Details}"))}

Provide troubleshooting recommendations.";

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a Windows system administration expert."),
                new UserChatMessage(prompt)
            };

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = _maxTokens
            };

            var response = await chatClient.CompleteChatAsync(messages, options);
            return response.Value.Content[0].Text;
        }
        catch (Exception ex)
        {
            return $"Error connecting to Copilot: {ex.Message}";
        }
    }

    public async Task<string> ChatAsync(string message, List<string>? history = null)
    {
        if (!IsConfigured)
            return "Copilot provider is not properly configured. Please check your API key and endpoint in Settings.";
            
        try
        {
            var azureClient = new AzureOpenAIClient(new Uri(_endpoint), new ApiKeyCredential(_apiKey));
            var chatClient = azureClient.GetChatClient(_model);
            
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a helpful Windows system administration assistant.")
            };

            if (history != null && history.Any())
            {
                for (int i = 0; i < history.Count; i++)
                {
                    if (i % 2 == 0)
                        messages.Add(new UserChatMessage(history[i]));
                    else
                        messages.Add(new AssistantChatMessage(history[i]));
                }
            }

            messages.Add(new UserChatMessage(message));

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = _maxTokens
            };

            var response = await chatClient.CompleteChatAsync(messages, options);
            return response.Value.Content[0].Text;
        }
        catch (Exception ex)
        {
            return $"Error connecting to Copilot: {ex.Message}";
        }
    }
}

public class ClaudeProvider : IAIProvider
{
    private readonly string _apiKey;
    private readonly string _model;
    private readonly HttpClient _httpClient = new();

    public string Id => "claude";
    public string Name => "Claude (Anthropic)";
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public ClaudeProvider(string apiKey, string model)
    {
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<string> AnalyzePatternAsync(DetectedPattern pattern, List<SystemEvent> events)
    {
        if (!IsConfigured)
            return "Claude provider is not properly configured. Please check your API key in Settings.";
            
        var prompt = $@"Analyze this system pattern:
Type: {pattern.PatternType}
Severity: {pattern.Severity}
Description: {pattern.Description}
Confidence: {pattern.Confidence:P0}

Recent events:
{string.Join("\n", events.Take(10).Select(e => $"- {e.Operation}: {e.Path ?? e.Details}"))}

Provide troubleshooting recommendations.";

        return await ChatAsync(prompt);
    }

    public async Task<string> ChatAsync(string message, List<string>? history = null)
    {
        if (!IsConfigured)
            return "Claude provider is not properly configured. Please check your API key in Settings.";
            
        try
        {
            var request = new
            {
                model = _model,
                max_tokens = 2000,
                messages = new[]
                {
                    new { role = "user", content = message }
                }
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var response = await _httpClient.PostAsJsonAsync(
                "https://api.anthropic.com/v1/messages",
                request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return $"Claude API error: {response.StatusCode} - {error}";
            }

            var result = await response.Content.ReadFromJsonAsync<ClaudeResponse>();
            return result?.Content?.FirstOrDefault()?.Text ?? "No response from Claude.";
        }
        catch (Exception ex)
        {
            return $"Error connecting to Claude: {ex.Message}";
        }
    }

    private class ClaudeResponse
    {
        public List<ContentItem>? Content { get; set; }
    }

    private class ContentItem
    {
        public string? Text { get; set; }
    }
}

public class ChatGPTProvider : IAIProvider
{
    private readonly string _apiKey;
    private readonly string _model;
    private readonly HttpClient _httpClient = new();

    public string Id => "chatgpt";
    public string Name => "ChatGPT (OpenAI)";
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public ChatGPTProvider(string apiKey, string model)
    {
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<string> AnalyzePatternAsync(DetectedPattern pattern, List<SystemEvent> events)
    {
        if (!IsConfigured)
            return "ChatGPT provider is not properly configured. Please check your API key in Settings.";
            
        var prompt = $@"Analyze this system pattern:
Type: {pattern.PatternType}
Severity: {pattern.Severity}
Description: {pattern.Description}
Confidence: {pattern.Confidence:P0}

Recent events:
{string.Join("\n", events.Take(10).Select(e => $"- {e.Operation}: {e.Path ?? e.Details}"))}

Provide troubleshooting recommendations.";

        return await ChatAsync(prompt);
    }

    public async Task<string> ChatAsync(string message, List<string>? history = null)
    {
        if (!IsConfigured)
            return "ChatGPT provider is not properly configured. Please check your API key in Settings.";
            
        try
        {
            var messages = new List<object>
            {
                new { role = "system", content = "You are a helpful Windows system administration assistant." }
            };

            if (history != null && history.Any())
            {
                for (int i = 0; i < history.Count; i++)
                {
                    var role = i % 2 == 0 ? "user" : "assistant";
                    messages.Add(new { role, content = history[i] });
                }
            }

            messages.Add(new { role = "user", content = message });

            var request = new
            {
                model = _model,
                messages
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.PostAsJsonAsync(
                "https://api.openai.com/v1/chat/completions",
                request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return $"OpenAI API error: {response.StatusCode} - {error}";
            }

            var result = await response.Content.ReadFromJsonAsync<OpenAIResponse>();
            return result?.Choices?.FirstOrDefault()?.Message?.Content ?? "No response from ChatGPT.";
        }
        catch (Exception ex)
        {
            return $"Error connecting to ChatGPT: {ex.Message}";
        }
    }

    private class OpenAIResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    private class Choice
    {
        public Message? Message { get; set; }
    }

    private class Message
    {
        public string? Content { get; set; }
    }
}

public class AIProviderService
{
    private readonly List<IAIProvider> _providers = new();
    private IAIProvider? _currentProvider;

    public IReadOnlyList<IAIProvider> AvailableProviders => _providers.AsReadOnly();
    public IAIProvider? CurrentProvider => _currentProvider;

    public void Initialize(AIProvidersConfiguration config)
    {
        try
        {
            _providers.Clear();

            // FIXED: Only add providers when explicitly enabled AND have required configuration
            if (config.Copilot.Enabled && 
                !string.IsNullOrEmpty(config.Copilot.ApiKey) && 
                !string.IsNullOrEmpty(config.Copilot.Endpoint))
            {
                try
                {
                    _providers.Add(new CopilotProvider(
                        config.Copilot.Endpoint,
                        config.Copilot.ApiKey,
                        config.Copilot.Model,
                        config.Copilot.MaxTokens
                    ));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error creating Copilot provider: {ex.Message}");
                }
            }

            if (config.Claude.Enabled && !string.IsNullOrEmpty(config.Claude.ApiKey))
            {
                try
                {
                    _providers.Add(new ClaudeProvider(
                        config.Claude.ApiKey,
                        config.Claude.Model
                    ));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error creating Claude provider: {ex.Message}");
                }
            }

            if (config.ChatGPT.Enabled && !string.IsNullOrEmpty(config.ChatGPT.ApiKey))
            {
                try
                {
                    _providers.Add(new ChatGPTProvider(
                        config.ChatGPT.ApiKey,
                        config.ChatGPT.Model
                    ));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error creating ChatGPT provider: {ex.Message}");
                }
            }

            _currentProvider = _providers.FirstOrDefault(p => p.IsConfigured);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in Initialize: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    public void SetCurrentProvider(string providerId)
    {
        _currentProvider = _providers.FirstOrDefault(p => p.Id == providerId);
    }

    public async Task<string> AnalyzePatternAsync(DetectedPattern pattern, List<SystemEvent> events)
    {
        if (_currentProvider == null || !_currentProvider.IsConfigured)
            return "No AI provider is configured. Please configure an AI provider in Settings.";

        return await _currentProvider.AnalyzePatternAsync(pattern, events);
    }

    public async Task<string> ChatAsync(string message, List<string>? history = null)
    {
        if (_currentProvider == null || !_currentProvider.IsConfigured)
            return "No AI provider is configured. Please configure an AI provider in Settings.";

        return await _currentProvider.ChatAsync(message, history);
    }
}

public class AIProvidersConfiguration
{
    public CopilotConfig Copilot { get; set; } = new();
    public ClaudeConfig Claude { get; set; } = new();
    public ChatGPTConfig ChatGPT { get; set; } = new();
}

public class CopilotConfig
{
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4";
    public int MaxTokens { get; set; } = 2000;
}

public class ClaudeConfig
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-3-5-sonnet-20241022";
}

public class ChatGPTConfig
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4";
}
