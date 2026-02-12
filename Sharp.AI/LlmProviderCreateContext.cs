namespace Sharp.AI;

public sealed record LlmProviderCreateContext(
    ModelDescriptor Model,
    string ApiKey,
    string BaseUrl,
    HttpMessageHandler? Handler = null,
    ILlmCredentialProvider? CredentialProvider = null,
    IReadOnlyDictionary<string, string>? Headers = null);
