using System.Text.Json;
using System.Text.Json.Serialization;
using Sharp.AI;

namespace Sharp.Core.Configuration;

[JsonConverter(typeof(ModelApiFormatJsonConverter))]
public enum ModelApiFormat
{
    OpenAiCompletions,
    OpenAiResponses,
    AnthropicMessages,
    GoogleGeminiCli
}

public sealed class ModelApiFormatJsonConverter : JsonConverter<ModelApiFormat>
{
    public override ModelApiFormat Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Model api format must be a string.");

        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new JsonException("Model api format cannot be empty.");

        return value.Trim() switch
        {
            "openai-completions" => ModelApiFormat.OpenAiCompletions,
            "openai-chat-completions" => ModelApiFormat.OpenAiCompletions,
            "openai-responses" => ModelApiFormat.OpenAiResponses,
            "anthropic-messages" => ModelApiFormat.AnthropicMessages,
            "google-gemini-cli" => ModelApiFormat.GoogleGeminiCli,
            // Backward compatibility for an early Sharp experimental value.
            "google-antigravity" => ModelApiFormat.GoogleGeminiCli,

            // Backward compatibility for old enum-style config values.
            "OpenAiCompletions" => ModelApiFormat.OpenAiCompletions,
            "OpenAiChatCompletions" => ModelApiFormat.OpenAiCompletions,
            "OpenAiResponses" => ModelApiFormat.OpenAiResponses,
            "AnthropicMessages" => ModelApiFormat.AnthropicMessages,
            "GoogleGeminiCli" => ModelApiFormat.GoogleGeminiCli,
            "GoogleAntigravity" => ModelApiFormat.GoogleGeminiCli,
            _ => throw new JsonException(
                $"Unsupported model api format '{value}'. Expected one of: " +
                "'openai-completions', 'openai-responses', 'anthropic-messages', 'google-gemini-cli'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, ModelApiFormat value, JsonSerializerOptions options)
    {
        var serialized = value switch
        {
            ModelApiFormat.OpenAiCompletions => "openai-completions",
            ModelApiFormat.OpenAiResponses => "openai-responses",
            ModelApiFormat.AnthropicMessages => "anthropic-messages",
            ModelApiFormat.GoogleGeminiCli => "google-gemini-cli",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported model API")
        };

        writer.WriteStringValue(serialized);
    }
}

public sealed class ModelConfig
{
    public string Id { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    // Legacy per-model API override (prefer ProviderConfig.Api).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ModelApiFormat? Api { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAiCompletionsCompatConfig? Compat { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ModelCapabilitiesConfig? Capabilities { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ModelPricingConfig? Pricing { get; set; }

    public int? ContextWindow { get; set; }
    public int? MaxOutputTokens { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Headers { get; set; }
}

public sealed class OpenAiCompletionsCompatConfig
{
    public bool? SupportsStore { get; set; }
    public bool? SupportsDeveloperRole { get; set; }
    public bool? SupportsReasoningEffort { get; set; }
    public bool? SupportsUsageInStreaming { get; set; }
    public bool? SupportsStrictMode { get; set; }
    public bool? RequiresToolResultName { get; set; }
    public bool? RequiresAssistantAfterToolResult { get; set; }
    public bool? RequiresMistralToolIds { get; set; }
    public bool? RequiresThinkingAsText { get; set; }
    public string? MaxTokensField { get; set; }
    public string? ThinkingFormat { get; set; }
    public OpenAiRoutingPreferences? OpenRouterRouting { get; set; }
    public OpenAiRoutingPreferences? VercelGatewayRouting { get; set; }
}

public sealed class ModelCapabilitiesConfig
{
    public bool? SupportsReasoning { get; set; }
    public bool? SupportsImageInput { get; set; }
    public bool? SupportsToolCall { get; set; }
}

public sealed class ModelPricingConfig
{
    public decimal? InputPerMillionTokens { get; set; }
    public decimal? OutputPerMillionTokens { get; set; }
    public decimal? CacheReadPerMillionTokens { get; set; }
    public decimal? CacheWritePerMillionTokens { get; set; }
}

public sealed class ProviderConfig
{
    public string Id { get; set; } = string.Empty;
    public ModelApiFormat Api { get; set; } = ModelApiFormat.OpenAiCompletions;
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public List<ModelConfig> Models { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Headers { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AuthHeader { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, ModelOverrideConfig>? ModelOverrides { get; set; }
}

public sealed class AgentConfig
{
    public string DefaultModel { get; set; } = "openai/gpt-4o-mini";
    public List<ProviderConfig> Providers { get; set; } = [..GetBuiltInProviders()];

    internal static IReadOnlyList<ProviderConfig> GetBuiltInProviders() =>
    [
        new ProviderConfig
        {
            Id = "openai",
            Api = ModelApiFormat.OpenAiCompletions,
            BaseUrl = "https://api.openai.com/v1/",
            Models =
            [
                new ModelConfig { Id = "gpt-4o-mini", ContextWindow = 128000, MaxOutputTokens = 16384 },
                new ModelConfig { Id = "gpt-4o", ContextWindow = 128000, MaxOutputTokens = 16384 }
            ]
        },
        new ProviderConfig
        {
            Id = "anthropic",
            Api = ModelApiFormat.AnthropicMessages,
            BaseUrl = "https://api.anthropic.com/v1/",
            Models =
            [
                new ModelConfig { Id = "claude-sonnet-4-20250514", ContextWindow = 200000, MaxOutputTokens = 8192 }
            ]
        },
        ..BuiltInPiProviders.Create()
    ];

    public static ProviderApiKind ToProviderApiKind(ModelApiFormat api)
        => api switch
        {
            ModelApiFormat.OpenAiCompletions => ProviderApiKind.OpenAiChatCompletions,
            ModelApiFormat.OpenAiResponses => ProviderApiKind.OpenAiResponses,
            ModelApiFormat.AnthropicMessages => ProviderApiKind.AnthropicMessages,
            ModelApiFormat.GoogleGeminiCli => ProviderApiKind.GoogleGeminiCli,
            _ => throw new ArgumentOutOfRangeException(nameof(api), api, "Unsupported model API")
        };

    public static OpenAiCompletionsCompat? ToOpenAiCompletionsCompat(OpenAiCompletionsCompatConfig? compat)
    {
        if (compat == null)
            return null;

        var maxTokensField = compat.MaxTokensField?.Trim().ToLowerInvariant() switch
        {
            null or "" => OpenAiMaxTokensField.MaxTokens,
            "max_tokens" or "maxtokens" => OpenAiMaxTokensField.MaxTokens,
            "max_completion_tokens" or "maxcompletiontokens" => OpenAiMaxTokensField.MaxCompletionTokens,
            _ => throw new JsonException(
                $"Unsupported compat.maxTokensField '{compat.MaxTokensField}'. Expected 'max_tokens' or 'max_completion_tokens'.")
        };

        return new OpenAiCompletionsCompat(
            SupportsUsageInStreaming: compat.SupportsUsageInStreaming ?? true,
            SupportsStrictMode: compat.SupportsStrictMode ?? true,
            RequiresToolResultName: compat.RequiresToolResultName ?? false,
            RequiresAssistantAfterToolResult: compat.RequiresAssistantAfterToolResult ?? false,
            RequiresMistralToolIds: compat.RequiresMistralToolIds ?? false,
            RequiresThinkingAsText: compat.RequiresThinkingAsText ?? false,
            MaxTokensField: maxTokensField,
            SupportsStore: compat.SupportsStore,
            SupportsDeveloperRole: compat.SupportsDeveloperRole,
            SupportsReasoningEffort: compat.SupportsReasoningEffort,
            ThinkingFormat: ParseThinkingFormat(compat.ThinkingFormat),
            OpenRouterRouting: NormalizeRouting(compat.OpenRouterRouting),
            VercelGatewayRouting: NormalizeRouting(compat.VercelGatewayRouting));
    }

    public static ModelCapabilities ToModelCapabilities(
        ModelApiFormat apiFormat,
        ModelCapabilitiesConfig? config)
    {
        var defaults = apiFormat switch
        {
            ModelApiFormat.OpenAiResponses => new ModelCapabilities(
                SupportsReasoning: true,
                SupportsImageInput: true,
                SupportsToolCall: true),
            ModelApiFormat.OpenAiCompletions => new ModelCapabilities(
                SupportsReasoning: true,
                SupportsImageInput: false,
                SupportsToolCall: true),
            ModelApiFormat.AnthropicMessages => new ModelCapabilities(
                SupportsReasoning: true,
                SupportsImageInput: false,
                SupportsToolCall: true),
            ModelApiFormat.GoogleGeminiCli => new ModelCapabilities(
                SupportsReasoning: true,
                SupportsImageInput: true,
                SupportsToolCall: true),
            _ => throw new ArgumentOutOfRangeException(nameof(apiFormat), apiFormat, "Unsupported model API")
        };

        if (config == null)
            return defaults;

        return defaults with
        {
            SupportsReasoning = config.SupportsReasoning ?? defaults.SupportsReasoning,
            SupportsImageInput = config.SupportsImageInput ?? defaults.SupportsImageInput,
            SupportsToolCall = config.SupportsToolCall ?? defaults.SupportsToolCall
        };
    }

    public static ModelPricing? ToModelPricing(ModelPricingConfig? config)
    {
        if (config == null)
            return null;

        var input = config.InputPerMillionTokens ?? 0m;
        var output = config.OutputPerMillionTokens ?? 0m;
        var cacheRead = config.CacheReadPerMillionTokens ?? 0m;
        var cacheWrite = config.CacheWritePerMillionTokens ?? 0m;

        if (input < 0m || output < 0m || cacheRead < 0m || cacheWrite < 0m)
            throw new JsonException("Model pricing values must be non-negative.");

        if (input == 0m && output == 0m && cacheRead == 0m && cacheWrite == 0m)
            return null;

        return new ModelPricing(
            InputPerMillionTokens: input,
            OutputPerMillionTokens: output,
            CacheReadPerMillionTokens: cacheRead,
            CacheWritePerMillionTokens: cacheWrite);
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

    private static OpenAiRoutingPreferences? NormalizeRouting(OpenAiRoutingPreferences? routing)
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
}
