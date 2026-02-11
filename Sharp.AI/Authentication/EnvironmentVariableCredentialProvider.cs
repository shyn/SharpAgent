namespace Sharp.AI.Authentication;

public sealed class EnvironmentVariableCredentialProvider : ILlmCredentialProvider
{
    private readonly ProviderApiKind _apiKind;
    private readonly IReadOnlyList<string> _tokenEnvironmentVariableCandidates;
    private readonly string? _fallbackToken;

    public EnvironmentVariableCredentialProvider(
        ProviderApiKind apiKind,
        IReadOnlyList<string> tokenEnvironmentVariableCandidates,
        string? fallbackToken = null)
    {
        _apiKind = apiKind;
        _tokenEnvironmentVariableCandidates = tokenEnvironmentVariableCandidates ?? [];
        _fallbackToken = fallbackToken;
    }

    public ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(
        LlmCredentialContext context,
        CancellationToken ct = default)
    {
        var token = ResolveToken();
        var headers = CredentialHeaderFactory.Create(_apiKind, token);
        return ValueTask.FromResult(headers);
    }

    private string? ResolveToken()
    {
        foreach (var name in _tokenEnvironmentVariableCandidates)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return _fallbackToken;
    }
}
