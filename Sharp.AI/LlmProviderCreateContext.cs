namespace Sharp.AI;

public sealed record LlmProviderCreateContext(
    ModelDescriptor Model,
    string ApiKey,
    string BaseUrl,
    HttpMessageHandler? Handler = null);
