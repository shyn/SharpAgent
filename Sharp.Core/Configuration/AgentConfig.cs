using System.Text.Json;
using System.Text.Json.Serialization;
using Sharp.AI;

namespace Sharp.Core.Configuration;

[JsonConverter(typeof(ModelApiFormatJsonConverter))]
public enum ModelApiFormat
{
    OpenAiCompletions,
    AnthropicMessages
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
            "anthropic-messages" => ModelApiFormat.AnthropicMessages,

            // Backward compatibility for old enum-style config values.
            "OpenAiCompletions" => ModelApiFormat.OpenAiCompletions,
            "OpenAiChatCompletions" => ModelApiFormat.OpenAiCompletions,
            "AnthropicMessages" => ModelApiFormat.AnthropicMessages,
            _ => throw new JsonException(
                $"Unsupported model api format '{value}'. Expected one of: " +
                "'openai-completions', 'anthropic-messages'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, ModelApiFormat value, JsonSerializerOptions options)
    {
        var serialized = value switch
        {
            ModelApiFormat.OpenAiCompletions => "openai-completions",
            ModelApiFormat.AnthropicMessages => "anthropic-messages",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported model API")
        };

        writer.WriteStringValue(serialized);
    }
}

public sealed class ModelConfig
{
    public string Id { get; set; } = string.Empty;

    // Legacy per-model API override (prefer ProviderConfig.Api).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ModelApiFormat? Api { get; set; }

    public int? ContextWindow { get; set; }
    public int? MaxOutputTokens { get; set; }
}

public sealed class ProviderConfig
{
    public string Id { get; set; } = string.Empty;
    public ModelApiFormat Api { get; set; } = ModelApiFormat.OpenAiCompletions;
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public List<ModelConfig> Models { get; set; } = [];
}

public sealed class AgentConfig
{
    public string DefaultModel { get; set; } = "openai/gpt-4o-mini";
    public List<ProviderConfig> Providers { get; set; } =
    [
        new ProviderConfig
        {
            Id = "openai",
            Api = ModelApiFormat.OpenAiCompletions,
            BaseUrl = "https://api.openai.com/v1/",
            Models =
            [
                new ModelConfig
                {
                    Id = "gpt-4o-mini",
                    ContextWindow = 128000,
                    MaxOutputTokens = 16384
                },
                new ModelConfig
                {
                    Id = "gpt-4o",
                    ContextWindow = 128000,
                    MaxOutputTokens = 16384
                }
            ]
        },
        new ProviderConfig
        {
            Id = "anthropic",
            Api = ModelApiFormat.AnthropicMessages,
            BaseUrl = "https://api.anthropic.com/v1/",
            Models =
            [
                new ModelConfig
                {
                    Id = "claude-sonnet-4-20250514",
                    ContextWindow = 200000,
                    MaxOutputTokens = 8192
                }
            ]
        }
    ];

    public static ProviderApiKind ToProviderApiKind(ModelApiFormat api)
        => api switch
        {
            ModelApiFormat.OpenAiCompletions => ProviderApiKind.OpenAiChatCompletions,
            ModelApiFormat.AnthropicMessages => ProviderApiKind.AnthropicMessages,
            _ => throw new ArgumentOutOfRangeException(nameof(api), api, "Unsupported model API")
        };
}
