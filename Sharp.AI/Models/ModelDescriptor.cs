namespace Sharp.AI.Models;

public sealed record ModelDescriptor(
    string ProviderId,
    string ModelId,
    ProviderApiKind ApiKind,
    int? ContextWindow = null,
    int? MaxOutputTokens = null,
    OpenAiCompletionsCompat? OpenAiCompletionsCompat = null,
    ModelCapabilities? Capabilities = null,
    ModelPricing? Pricing = null,
    string? DisplayName = null,
    IReadOnlyDictionary<string, string>? Headers = null);
