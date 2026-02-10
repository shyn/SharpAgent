namespace Sharp.AI;

public sealed record ModelDescriptor(
    string ProviderId,
    string ModelId,
    ProviderApiKind ApiKind,
    int? ContextWindow = null,
    int? MaxOutputTokens = null);
