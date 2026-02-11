namespace Sharp.AI;

public sealed class CachingBearerCredentialProvider : ILlmCredentialProvider, IDisposable
{
    private readonly ProviderApiKind _apiKind;
    private readonly ILlmBearerTokenSource _tokenSource;
    private readonly TimeSpan _refreshSkew;
    private readonly bool _cacheTokensWithoutExpiry;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private CachedToken? _cachedToken;

    public CachingBearerCredentialProvider(
        ProviderApiKind apiKind,
        ILlmBearerTokenSource tokenSource,
        TimeSpan? refreshSkew = null,
        bool cacheTokensWithoutExpiry = true)
    {
        if (refreshSkew is { } skew && skew < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(refreshSkew), refreshSkew, "Refresh skew cannot be negative.");

        _apiKind = apiKind;
        _tokenSource = tokenSource ?? throw new ArgumentNullException(nameof(tokenSource));
        _refreshSkew = refreshSkew ?? TimeSpan.FromMinutes(1);
        _cacheTokensWithoutExpiry = cacheTokensWithoutExpiry;
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(
        LlmCredentialContext context,
        CancellationToken ct = default)
    {
        var token = await GetTokenAsync(context, ct);
        return CredentialHeaderFactory.Create(_apiKind, token);
    }

    private async ValueTask<string?> GetTokenAsync(LlmCredentialContext context, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var current = _cachedToken;
        if (IsTokenUsable(current, now))
            return current!.Value;

        await _refreshLock.WaitAsync(ct);
        try
        {
            now = DateTimeOffset.UtcNow;
            current = _cachedToken;
            if (IsTokenUsable(current, now))
                return current!.Value;

            var refreshed = await _tokenSource.GetTokenAsync(context, ct);
            if (refreshed is null || string.IsNullOrWhiteSpace(refreshed.Value))
            {
                _cachedToken = null;
                return null;
            }

            if (!_cacheTokensWithoutExpiry && refreshed.ExpiresAt is null)
            {
                _cachedToken = null;
                return refreshed.Value;
            }

            _cachedToken = new CachedToken(refreshed.Value, refreshed.ExpiresAt);
            return refreshed.Value;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsTokenUsable(CachedToken? token, DateTimeOffset now)
    {
        if (token is null || string.IsNullOrWhiteSpace(token.Value))
            return false;

        if (token.ExpiresAt is null)
            return _cacheTokensWithoutExpiry;

        return token.ExpiresAt.Value > now + _refreshSkew;
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
        if (_tokenSource is IDisposable disposable)
            disposable.Dispose();
    }

    private sealed record CachedToken(string Value, DateTimeOffset? ExpiresAt);
}
