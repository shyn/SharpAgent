using System.Text.Json.Serialization;

namespace SharpAgent.Core.Configuration;

/// <summary>
/// Supported API formats for LLM providers.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ApiFormat
{
    OpenAI,
    Anthropic,
    ResponseApi  // Future: OpenAI Response API
}

/// <summary>
/// Model capabilities configuration.
/// </summary>
public sealed class LlmCapabilities
{
    public bool ToolCall { get; set; } = true;
    public bool Image { get; set; } = false;
    public bool Thinking { get; set; } = false;
    public bool Temperature { get; set; } = true;
    public bool ReasoningEffort { get; set; } = false;
}

/// <summary>
/// Configuration for a specific LLM model.
/// </summary>
public sealed class LlmModelConfig
{
    public string Id { get; set; } = string.Empty;
    public List<ApiFormat> ApiFormats { get; set; } = [ApiFormat.OpenAI];
    public int? ContextWindow { get; set; }
    public int? MaxOutputTokens { get; set; }
    public LlmCapabilities Capabilities { get; set; } = new();
}

/// <summary>
/// Configuration for an LLM provider containing multiple models.
/// </summary>
public sealed class LlmProviderConfig
{
    public string Id { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public List<LlmModelConfig> Models { get; set; } = [];
}

/// <summary>
/// Main agent configuration with providers and models.
/// </summary>
public sealed class AgentConfig
{
    /// <summary>
    /// Default model in "provider/model" format (e.g., "openai/gpt-4o-mini").
    /// </summary>
    public string DefaultModel { get; set; } = "openai/gpt-4o-mini";

    /// <summary>
    /// List of configured LLM providers.
    /// </summary>
    public List<LlmProviderConfig> Providers { get; set; } = GetDefaultProviders();

    private static List<LlmProviderConfig> GetDefaultProviders() =>
    [
        new LlmProviderConfig
        {
            Id = "openai",
            BaseUrl = "https://api.openai.com/v1/",
            Models =
            [
                new LlmModelConfig
                {
                    Id = "gpt-4o-mini",
                    ApiFormats = [ApiFormat.OpenAI],
                    ContextWindow = 128000,
                    MaxOutputTokens = 16384,
                    Capabilities = new LlmCapabilities { ToolCall = true, Image = true }
                },
                new LlmModelConfig
                {
                    Id = "gpt-4o",
                    ApiFormats = [ApiFormat.OpenAI],
                    ContextWindow = 128000,
                    MaxOutputTokens = 16384,
                    Capabilities = new LlmCapabilities { ToolCall = true, Image = true }
                }
            ]
        },
        new LlmProviderConfig
        {
            Id = "anthropic",
            BaseUrl = "https://api.anthropic.com/v1/",
            Models =
            [
                new LlmModelConfig
                {
                    Id = "claude-sonnet-4-20250514",
                    ApiFormats = [ApiFormat.Anthropic],
                    ContextWindow = 200000,
                    MaxOutputTokens = 8192,
                    Capabilities = new LlmCapabilities { ToolCall = true, Image = true, Thinking = true }
                }
            ]
        }
    ];
}

public enum ThinkingLevel { Off, Low, Middle, High }

/// <summary>
/// Runtime configuration for extended thinking mode.
/// This is not persisted - it's set dynamically at runtime.
/// </summary>
public sealed class ThinkingConfig
{
    public static ThinkingConfig Disabled => new() { Level = ThinkingLevel.Off };
    
    public ThinkingLevel Level { get; set; } = ThinkingLevel.Off;
    
    /// <summary>
    /// Returns true if thinking is enabled (Level != Off)
    /// </summary>
    [JsonIgnore]
    public bool Enabled => Level != ThinkingLevel.Off;

    /// <summary>
    /// Token budget for thinking based on the level.
    /// </summary>
    public int BudgetTokens => Level switch
    {
        ThinkingLevel.Low => 4096,
        ThinkingLevel.Middle => 16384,
        ThinkingLevel.High => 65536,
        _ => 0
    };
}
