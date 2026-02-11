namespace Sharp.AI;

public sealed record ModelCapabilities(
    bool SupportsReasoning = true,
    bool SupportsImageInput = false,
    bool SupportsToolCall = true);

public sealed record ModelPricing(
    decimal InputPerMillionTokens,
    decimal OutputPerMillionTokens,
    decimal CacheReadPerMillionTokens = 0m,
    decimal CacheWritePerMillionTokens = 0m);
