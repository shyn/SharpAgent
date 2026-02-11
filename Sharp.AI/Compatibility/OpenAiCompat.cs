namespace Sharp.AI.Compatibility;

public enum OpenAiMaxTokensField
{
    MaxTokens,
    MaxCompletionTokens
}

public static class OpenAiThinkingFormats
{
    public const string OpenAi = "openai";
    public const string Zai = "zai";
    public const string Qwen = "qwen";
}

public sealed record OpenAiRoutingPreferences(
    string[]? Only = null,
    string[]? Order = null);

public sealed record OpenAiCompletionsCompat(
    bool SupportsUsageInStreaming = true,
    bool SupportsStrictMode = true,
    bool RequiresToolResultName = false,
    bool RequiresAssistantAfterToolResult = false,
    bool RequiresMistralToolIds = false,
    bool RequiresThinkingAsText = false,
    OpenAiMaxTokensField MaxTokensField = OpenAiMaxTokensField.MaxTokens,
    bool? SupportsStore = null,
    bool? SupportsDeveloperRole = null,
    bool? SupportsReasoningEffort = null,
    string? ThinkingFormat = null,
    OpenAiRoutingPreferences? OpenRouterRouting = null,
    OpenAiRoutingPreferences? VercelGatewayRouting = null);
