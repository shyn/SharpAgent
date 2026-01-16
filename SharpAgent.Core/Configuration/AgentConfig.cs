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
