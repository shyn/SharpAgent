namespace Sharp.AI.Contracts;

public sealed record LlmCredentialContext(
    ModelDescriptor Model,
    string BaseUrl);

public interface ILlmCredentialProvider
{
    ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(
        LlmCredentialContext context,
        CancellationToken ct = default);
}
