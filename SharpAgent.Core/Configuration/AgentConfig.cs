using System.Text.Json.Serialization;

namespace SharpAgent.Core.Configuration;

public sealed class AgentConfig
{
    public string Provider { get; set; } = "openai";
    public OpenAiConfig OpenAi { get; set; } = new();
    public AnthropicConfig Anthropic { get; set; } = new();
}

public sealed class OpenAiConfig
{
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string Model { get; set; } = "gpt-4o-mini";
}

public sealed class AnthropicConfig
{
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/";
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public int MaxTokens { get; set; } = 8192;
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
