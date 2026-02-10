using System.Text.Json;
using Sharp.AI;

namespace Sharp.Core.Configuration;

public static class OpenAiCompatResolver
{
    public static OpenAiCompletionsCompat ResolveCompletionsCompat(
        string providerId,
        string baseUrl,
        OpenAiCompletionsCompatConfig? explicitCompat)
    {
        var detected = DetectCompletionsCompat(providerId, baseUrl);
        if (explicitCompat == null)
            return detected;

        return new OpenAiCompletionsCompat(
            SupportsUsageInStreaming: explicitCompat.SupportsUsageInStreaming ?? detected.SupportsUsageInStreaming,
            SupportsStrictMode: explicitCompat.SupportsStrictMode ?? detected.SupportsStrictMode,
            RequiresToolResultName: explicitCompat.RequiresToolResultName ?? detected.RequiresToolResultName,
            RequiresAssistantAfterToolResult:
            explicitCompat.RequiresAssistantAfterToolResult ?? detected.RequiresAssistantAfterToolResult,
            RequiresMistralToolIds: explicitCompat.RequiresMistralToolIds ?? detected.RequiresMistralToolIds,
            RequiresThinkingAsText: explicitCompat.RequiresThinkingAsText ?? detected.RequiresThinkingAsText,
            MaxTokensField: ParseMaxTokensField(explicitCompat.MaxTokensField) ?? detected.MaxTokensField,
            SupportsStore: explicitCompat.SupportsStore ?? detected.SupportsStore,
            SupportsDeveloperRole: explicitCompat.SupportsDeveloperRole ?? detected.SupportsDeveloperRole,
            SupportsReasoningEffort: explicitCompat.SupportsReasoningEffort ?? detected.SupportsReasoningEffort,
            ThinkingFormat: ParseThinkingFormat(explicitCompat.ThinkingFormat) ?? detected.ThinkingFormat,
            OpenRouterRouting: ParseRouting(explicitCompat.OpenRouterRouting) ?? detected.OpenRouterRouting,
            VercelGatewayRouting: ParseRouting(explicitCompat.VercelGatewayRouting) ?? detected.VercelGatewayRouting);
    }

    private static OpenAiCompletionsCompat DetectCompletionsCompat(string providerId, string baseUrl)
    {
        var isZai = IsProvider(providerId, "zai") || ContainsIgnoreCase(baseUrl, "api.z.ai");
        var isQwen = IsProvider(providerId, "qwen") || ContainsIgnoreCase(baseUrl, "dashscope.aliyuncs.com");
        var isXai = IsProvider(providerId, "xai") || ContainsIgnoreCase(baseUrl, "api.x.ai");
        var isGrok = isXai;
        var isMistral = IsProvider(providerId, "mistral") || ContainsIgnoreCase(baseUrl, "mistral.ai");
        var isChutes = ContainsIgnoreCase(baseUrl, "chutes.ai");
        var isGatewayz = ContainsIgnoreCase(baseUrl, "gatewayz.ai");
        var isNonStandard = IsProvider(providerId, "cerebras")
                            || IsProvider(providerId, "mistral")
                            || IsProvider(providerId, "opencode")
                            || ContainsIgnoreCase(baseUrl, "cerebras.ai")
                            || isXai
                            || ContainsIgnoreCase(baseUrl, "chutes.ai")
                            || ContainsIgnoreCase(baseUrl, "deepseek.com")
                            || ContainsIgnoreCase(baseUrl, "opencode.ai")
                            || isZai
                            || isMistral;

        return new OpenAiCompletionsCompat(
            SupportsUsageInStreaming: !isGatewayz,
            SupportsStrictMode: true,
            RequiresToolResultName: isMistral,
            RequiresAssistantAfterToolResult: false,
            RequiresMistralToolIds: isMistral,
            RequiresThinkingAsText: isMistral,
            MaxTokensField: isMistral || isChutes
                ? OpenAiMaxTokensField.MaxTokens
                : OpenAiMaxTokensField.MaxCompletionTokens,
            SupportsStore: !isNonStandard,
            SupportsDeveloperRole: !isNonStandard,
            SupportsReasoningEffort: !isGrok && !isZai,
            ThinkingFormat: isZai
                ? OpenAiThinkingFormats.Zai
                : isQwen
                    ? OpenAiThinkingFormats.Qwen
                    : OpenAiThinkingFormats.OpenAi,
            OpenRouterRouting: null,
            VercelGatewayRouting: null);
    }

    private static OpenAiMaxTokensField? ParseMaxTokensField(string? rawValue)
    {
        return rawValue?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "max_tokens" or "maxtokens" => OpenAiMaxTokensField.MaxTokens,
            "max_completion_tokens" or "maxcompletiontokens" => OpenAiMaxTokensField.MaxCompletionTokens,
            _ => throw new JsonException(
                $"Unsupported compat.maxTokensField '{rawValue}'. Expected 'max_tokens' or 'max_completion_tokens'.")
        };
    }

    private static string? ParseThinkingFormat(string? rawValue)
    {
        var normalized = rawValue?.Trim().ToLowerInvariant();
        return normalized switch
        {
            null or "" => null,
            OpenAiThinkingFormats.OpenAi => OpenAiThinkingFormats.OpenAi,
            OpenAiThinkingFormats.Zai => OpenAiThinkingFormats.Zai,
            OpenAiThinkingFormats.Qwen => OpenAiThinkingFormats.Qwen,
            _ => throw new JsonException(
                $"Unsupported compat.thinkingFormat '{rawValue}'. Expected 'openai', 'zai', or 'qwen'.")
        };
    }

    private static OpenAiRoutingPreferences? ParseRouting(OpenAiRoutingPreferences? routing)
    {
        if (routing == null)
            return null;

        var only = NormalizeRoutingValues(routing.Only);
        var order = NormalizeRoutingValues(routing.Order);
        if (only == null && order == null)
            return null;

        return new OpenAiRoutingPreferences(Only: only, Order: order);
    }

    private static string[]? NormalizeRoutingValues(string[]? values)
    {
        if (values == null || values.Length == 0)
            return null;

        var normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return normalized.Length == 0 ? null : normalized;
    }

    private static bool IsProvider(string providerId, string expected)
        => providerId.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsIgnoreCase(string value, string token)
        => value.Contains(token, StringComparison.OrdinalIgnoreCase);
}
