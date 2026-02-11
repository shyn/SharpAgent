namespace Sharp.AI;

public sealed class StaticApiKeyCredentialProvider : ILlmCredentialProvider
{
    private readonly string _apiKey;

    public StaticApiKeyCredentialProvider(string apiKey)
    {
        _apiKey = apiKey;
    }

    public ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(
        LlmCredentialContext context,
        CancellationToken ct = default)
    {
        var headers = CredentialHeaderFactory.Create(context.Model.ApiKind, _apiKey);
        return ValueTask.FromResult(headers);
    }
}
