namespace Sharp.AI.Authentication;

internal static class CredentialHeaderFactory
{
    public static IReadOnlyDictionary<string, string> Create(ProviderApiKind apiKind, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return apiKind switch
        {
            ProviderApiKind.AnthropicMessages => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-api-key"] = token,
                ["anthropic-version"] = "2023-06-01"
            },
            _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] = $"Bearer {token}"
            }
        };
    }
}
