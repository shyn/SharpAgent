namespace Sharp.AI;

public sealed record LlmBearerToken(
    string Value,
    DateTimeOffset? ExpiresAt = null);

public interface ILlmBearerTokenSource
{
    ValueTask<LlmBearerToken?> GetTokenAsync(
        LlmCredentialContext context,
        CancellationToken ct = default);
}
