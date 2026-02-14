using Sharp.AI;

namespace Sharp.Core.Tests;

public sealed class CachingBearerCredentialProviderTests
{
    [Fact]
    public async Task GetHeadersAsync_BeforeExpiry_UsesCache()
    {
        var source = new SequentialTokenSource(
            new LlmBearerToken("token-1", DateTimeOffset.UtcNow.AddMinutes(10)),
            new LlmBearerToken("token-2", DateTimeOffset.UtcNow.AddMinutes(10)));

        using var provider = new CachingBearerCredentialProvider(
            ProviderApiKind.OpenAiChatCompletions,
            source,
            refreshSkew: TimeSpan.FromMinutes(1));

        var context = CreateContext(ProviderApiKind.OpenAiChatCompletions);

        var first = await provider.GetHeadersAsync(context);
        var second = await provider.GetHeadersAsync(context);

        Assert.Equal("Bearer token-1", first["Authorization"]);
        Assert.Equal("Bearer token-1", second["Authorization"]);
        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public async Task GetHeadersAsync_NearExpiry_RefreshesToken()
    {
        var source = new SequentialTokenSource(
            new LlmBearerToken("token-1", DateTimeOffset.UtcNow.AddSeconds(30)),
            new LlmBearerToken("token-2", DateTimeOffset.UtcNow.AddMinutes(10)));

        using var provider = new CachingBearerCredentialProvider(
            ProviderApiKind.OpenAiResponses,
            source,
            refreshSkew: TimeSpan.FromMinutes(1));

        var context = CreateContext(ProviderApiKind.OpenAiResponses);

        var first = await provider.GetHeadersAsync(context);
        var second = await provider.GetHeadersAsync(context);

        Assert.Equal("Bearer token-1", first["Authorization"]);
        Assert.Equal("Bearer token-2", second["Authorization"]);
        Assert.Equal(2, source.CallCount);
    }

    [Fact]
    public async Task GetHeadersAsync_ConcurrentNearExpiryRefresh_OnlyFetchesOnce()
    {
        var source = new SequentialTokenSource(
            TimeSpan.FromMilliseconds(50),
            new LlmBearerToken("seed-token", DateTimeOffset.UtcNow.AddSeconds(30)),
            new LlmBearerToken("fresh-token", DateTimeOffset.UtcNow.AddMinutes(10)),
            new LlmBearerToken("should-not-be-used", DateTimeOffset.UtcNow.AddMinutes(10)));

        using var provider = new CachingBearerCredentialProvider(
            ProviderApiKind.OpenAiChatCompletions,
            source,
            refreshSkew: TimeSpan.FromMinutes(1));

        var context = CreateContext(ProviderApiKind.OpenAiChatCompletions);

        var seed = await provider.GetHeadersAsync(context);
        Assert.Equal("Bearer seed-token", seed["Authorization"]);
        Assert.Equal(1, source.CallCount);

        var tasks = Enumerable.Range(0, 24)
            .Select(_ => provider.GetHeadersAsync(context).AsTask())
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.All(tasks, task => Assert.Equal("Bearer fresh-token", task.Result["Authorization"]));
        Assert.Equal(2, source.CallCount);
    }

    [Fact]
    public async Task GetHeadersAsync_NonExpiringTokenCacheDisabled_RefreshesEachRequest()
    {
        var source = new SequentialTokenSource(
            new LlmBearerToken("token-1"),
            new LlmBearerToken("token-2"));

        using var provider = new CachingBearerCredentialProvider(
            ProviderApiKind.OpenAiChatCompletions,
            source,
            cacheTokensWithoutExpiry: false);

        var context = CreateContext(ProviderApiKind.OpenAiChatCompletions);

        var first = await provider.GetHeadersAsync(context);
        var second = await provider.GetHeadersAsync(context);

        Assert.Equal("Bearer token-1", first["Authorization"]);
        Assert.Equal("Bearer token-2", second["Authorization"]);
        Assert.Equal(2, source.CallCount);
    }

    [Theory]
    [InlineData(ProviderApiKind.OpenAiChatCompletions)]
    [InlineData(ProviderApiKind.OpenAiResponses)]
    public async Task GetHeadersAsync_OpenAiKinds_UseAuthorizationHeader(ProviderApiKind apiKind)
    {
        var source = new SequentialTokenSource(new LlmBearerToken("token", DateTimeOffset.UtcNow.AddMinutes(10)));
        using var provider = new CachingBearerCredentialProvider(apiKind, source);

        var headers = await provider.GetHeadersAsync(CreateContext(apiKind));

        Assert.Equal("Bearer token", headers["Authorization"]);
        Assert.False(headers.ContainsKey("x-api-key"));
        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public async Task GetHeadersAsync_Anthropic_UsesApiKeyHeaders()
    {
        var source = new SequentialTokenSource(new LlmBearerToken("token", DateTimeOffset.UtcNow.AddMinutes(10)));
        using var provider = new CachingBearerCredentialProvider(ProviderApiKind.AnthropicMessages, source);

        var headers = await provider.GetHeadersAsync(CreateContext(ProviderApiKind.AnthropicMessages));

        Assert.Equal("token", headers["x-api-key"]);
        Assert.Equal("2023-06-01", headers["anthropic-version"]);
        Assert.False(headers.ContainsKey("Authorization"));
        Assert.Equal(1, source.CallCount);
    }

    private static LlmCredentialContext CreateContext(ProviderApiKind apiKind)
        => new(
            new ModelDescriptor("provider", "model", apiKind),
            "https://example.com/v1/");

    private sealed class SequentialTokenSource : ILlmBearerTokenSource
    {
        private readonly Queue<LlmBearerToken?> _tokens;
        private readonly TimeSpan _delay;
        private int _callCount;

        public SequentialTokenSource(params LlmBearerToken?[] tokens)
            : this(TimeSpan.Zero, tokens)
        {
        }

        public SequentialTokenSource(TimeSpan delay, params LlmBearerToken?[] tokens)
        {
            _tokens = new Queue<LlmBearerToken?>(tokens);
            _delay = delay;
        }

        public int CallCount => Volatile.Read(ref _callCount);

        public async ValueTask<LlmBearerToken?> GetTokenAsync(LlmCredentialContext context, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, ct);

            lock (_tokens)
            {
                return _tokens.Count == 0 ? null : _tokens.Dequeue();
            }
        }
    }
}
