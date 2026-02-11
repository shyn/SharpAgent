using System.Net;
using System.Text;
using Sharp.AI;

namespace Sharp.Core.Tests;

public sealed class AntigravityBearerTokenSourceTests
{
    [Fact]
    public async Task GetTokenAsync_OAuthCredentialEnvelope_UsesAccessAndExpiry()
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeMilliseconds();
        using var source = new AntigravityBearerTokenSource(
            $"{{\"access\":\"oauth-token\",\"refresh\":\"refresh-token\",\"expires\":{expires},\"projectId\":\"proj-1\"}}");

        var token = await source.GetTokenAsync(CreateContext());

        Assert.NotNull(token);
        Assert.Equal("oauth-token", token!.Value);
        Assert.NotNull(token.ExpiresAt);
        Assert.True(token.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GetTokenAsync_ExpiredCredential_RefreshesViaGoogleOAuthEndpoint()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"refreshed-token","expires_in":3600}""",
                Encoding.UTF8,
                "application/json")
        });

        var expired = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds();
        using var source = new AntigravityBearerTokenSource(
            $"{{\"access\":\"stale-token\",\"refresh\":\"refresh-token\",\"expires\":{expired},\"projectId\":\"proj-1\"}}",
            handler);

        var token = await source.GetTokenAsync(CreateContext());

        Assert.NotNull(token);
        Assert.Equal("refreshed-token", token!.Value);
        Assert.NotNull(token.ExpiresAt);
        Assert.True(token.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.Equal(1, handler.RequestCount);
        Assert.Contains("oauth2.googleapis.com/token", handler.LastRequestUrl);
        Assert.Contains("refresh_token=refresh-token", handler.LastRequestBody);
    }

    [Fact]
    public async Task GetTokenAsync_PlainToken_ReturnsWithoutRefresh()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("should not call network"));
        using var source = new AntigravityBearerTokenSource("plain-token", handler);

        var token = await source.GetTokenAsync(CreateContext());

        Assert.NotNull(token);
        Assert.Equal("plain-token", token!.Value);
        Assert.Null(token.ExpiresAt);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task GetTokenAsync_ExpiredCredential_RefreshFailure_Throws()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("invalid_grant", Encoding.UTF8, "text/plain")
        });

        var expired = DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeMilliseconds();
        using var source = new AntigravityBearerTokenSource(
            $"{{\"access\":\"stale-token\",\"refresh\":\"refresh-token\",\"expires\":{expired},\"projectId\":\"proj-1\"}}",
            handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => source.GetTokenAsync(CreateContext()).AsTask());
        Assert.Contains("refresh", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetTokenAsync_ExpiredCredential_UsesTokenEndpointOverride()
    {
        const string endpointOverride = "http://127.0.0.1:18080/oauth2/token";
        var envName = "SHARP_ANTIGRAVITY_OAUTH_TOKEN_ENDPOINT";
        var previous = Environment.GetEnvironmentVariable(envName);
        Environment.SetEnvironmentVariable(envName, endpointOverride);

        try
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"refreshed-token","expires_in":3600}""",
                    Encoding.UTF8,
                    "application/json")
            });

            var expired = DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeMilliseconds();
            using var source = new AntigravityBearerTokenSource(
                $"{{\"access\":\"stale-token\",\"refresh\":\"refresh-token\",\"expires\":{expired},\"projectId\":\"proj-1\"}}",
                handler);

            var token = await source.GetTokenAsync(CreateContext());
            Assert.NotNull(token);
            Assert.Equal("refreshed-token", token!.Value);
            Assert.Equal(endpointOverride, handler.LastRequestUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, previous);
        }
    }

    private static LlmCredentialContext CreateContext()
        => new(new ModelDescriptor("google-antigravity", "gemini-3-flash", ProviderApiKind.GoogleGeminiCli), "https://daily-cloudcode-pa.sandbox.googleapis.com/");

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int RequestCount { get; private set; }
        public string LastRequestUrl { get; private set; } = string.Empty;
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUrl = request.RequestUri?.ToString() ?? string.Empty;
            LastRequestBody = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responseFactory(request);
        }
    }
}
