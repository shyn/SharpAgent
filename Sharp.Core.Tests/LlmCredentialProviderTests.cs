using System.Net;
using System.Text;
using Sharp.AI;

namespace Sharp.Core.Tests;

public sealed class LlmCredentialProviderTests
{
    [Fact]
    public async Task Factory_CustomCredentialProvider_RefreshesAuthorizationPerRequest()
    {
        var handler = new CaptureHeadersHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("data: [DONE]\n", Encoding.UTF8, "text/event-stream")
        });

        var rotating = new RotatingBearerCredentialProvider("token-1", "token-2");
        using var provider = LlmProviderFactory.Create(
            model: new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            apiKey: string.Empty,
            baseUrl: "https://api.openai.com/v1/",
            credentialProvider: rotating,
            handler: handler);

        var request = new LlmRequest(
            Model: new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools: []);

        var first = await CollectAsync(provider.StreamAsync(request));
        Assert.IsType<LlmCompletedEvent>(first.Last());

        var second = await CollectAsync(provider.StreamAsync(request));
        Assert.IsType<LlmCompletedEvent>(second.Last());

        Assert.Equal(2, handler.AuthorizationValues.Count);
        Assert.Equal("Bearer token-1", handler.AuthorizationValues[0]);
        Assert.Equal("Bearer token-2", handler.AuthorizationValues[1]);
    }

    [Fact]
    public async Task Factory_StaticApiKeyCredentialProvider_AddsAnthropicHeaders()
    {
        var sse = string.Join(
            "\n",
            [
                "event: content_block_start",
                "data: {\"index\":0,\"content_block\":{\"type\":\"text\"}}",
                "",
                "event: content_block_delta",
                "data: {\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"ok\"}}",
                "",
                "event: content_block_stop",
                "data: {\"index\":0}",
                "",
                "event: message_stop",
                "data: {\"type\":\"message_stop\"}"
            ]);

        var handler = new CaptureHeadersHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });

        using var provider = LlmProviderFactory.Create(
            model: new ModelDescriptor("anthropic", "claude-sonnet-4-20250514", ProviderApiKind.AnthropicMessages),
            apiKey: "anthropic-key",
            baseUrl: "https://api.anthropic.com/v1/",
            handler: handler);

        var request = new LlmRequest(
            Model: new ModelDescriptor("anthropic", "claude-sonnet-4-20250514", ProviderApiKind.AnthropicMessages),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.IsType<LlmCompletedEvent>(events.Last());

        Assert.Single(handler.ApiKeyValues);
        Assert.Equal("anthropic-key", handler.ApiKeyValues[0]);

        Assert.Single(handler.AnthropicVersionValues);
        Assert.Equal("2023-06-01", handler.AnthropicVersionValues[0]);
    }

    [Fact]
    public async Task Factory_GoogleAntigravity_UsesEnvelopeTokenAndProjectId()
    {
        var sse = string.Join(
            "\n",
            [
                "data: {\"response\":{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"ok\"}]},\"finishReason\":\"STOP\"}],\"usageMetadata\":{\"promptTokenCount\":10,\"candidatesTokenCount\":2,\"cachedContentTokenCount\":1}}}"
            ]);

        var handler = new CaptureHeadersHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });

        using var provider = LlmProviderFactory.Create(
            model: new ModelDescriptor("google-antigravity", "gemini-3-flash", ProviderApiKind.GoogleGeminiCli),
            apiKey: """{"token":"oauth-token","projectId":"proj-123"}""",
            baseUrl: "https://daily-cloudcode-pa.sandbox.googleapis.com/",
            handler: handler);

        var request = new LlmRequest(
            Model: new ModelDescriptor("google-antigravity", "gemini-3-flash", ProviderApiKind.GoogleGeminiCli),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        var completed = Assert.IsType<LlmCompletedEvent>(events.Last());
        Assert.Equal("ok", completed.FullText);

        Assert.Single(handler.AuthorizationValues);
        Assert.Equal("Bearer oauth-token", handler.AuthorizationValues[0]);
        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"project\":\"proj-123\"", handler.LastRequestBody!);
    }

    [Fact]
    public async Task Factory_GoogleAntigravity_OAuthRefresh_EndToEnd_UsesRefreshedToken()
    {
        var refreshHandler = new CaptureRefreshHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"refreshed-token","expires_in":3600}""",
                Encoding.UTF8,
                "application/json")
        });

        var expired = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds();
        var oauthEnvelope =
            $"{{\"access\":\"stale-token\",\"refresh\":\"refresh-token\",\"expires\":{expired},\"projectId\":\"proj-456\"}}";
        using var credentialProvider = new CachingBearerCredentialProvider(
            ProviderApiKind.GoogleGeminiCli,
            new AntigravityBearerTokenSource(
                oauthEnvelope,
                refreshHandler),
            cacheTokensWithoutExpiry: false);

        var sse = string.Join(
            "\n",
            [
                "data: {\"response\":{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"ok\"}]},\"finishReason\":\"STOP\"}],\"usageMetadata\":{\"promptTokenCount\":10,\"candidatesTokenCount\":2,\"cachedContentTokenCount\":1}}}"
            ]);

        var streamHandler = new CaptureHeadersHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });

        using var provider = LlmProviderFactory.Create(
            model: new ModelDescriptor("google-antigravity", "claude-opus-4-6-thinking", ProviderApiKind.GoogleGeminiCli),
            apiKey: oauthEnvelope,
            baseUrl: "https://daily-cloudcode-pa.sandbox.googleapis.com/",
            credentialProvider: credentialProvider,
            handler: streamHandler);

        var request = new LlmRequest(
            Model: new ModelDescriptor("google-antigravity", "claude-opus-4-6-thinking", ProviderApiKind.GoogleGeminiCli),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        var completed = Assert.IsType<LlmCompletedEvent>(events.Last());

        Assert.Equal("ok", completed.FullText);
        Assert.Equal(1, refreshHandler.RequestCount);
        Assert.Contains("oauth2.googleapis.com/token", refreshHandler.LastRequestUrl);
        Assert.Contains("refresh_token=refresh-token", refreshHandler.LastRequestBody);
        Assert.Single(streamHandler.AuthorizationValues);
        Assert.Equal("Bearer refreshed-token", streamHandler.AuthorizationValues[0]);
        Assert.NotNull(streamHandler.LastRequestBody);
        Assert.Contains("\"project\":\"proj-456\"", streamHandler.LastRequestBody!);
    }

    private static async Task<List<LlmStreamEvent>> CollectAsync(IAsyncEnumerable<LlmStreamEvent> stream)
    {
        var events = new List<LlmStreamEvent>();
        await foreach (var item in stream)
            events.Add(item);

        return events;
    }

    private sealed class CaptureHeadersHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public CaptureHeadersHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<string?> AuthorizationValues { get; } = [];
        public List<string?> ApiKeyValues { get; } = [];
        public List<string?> AnthropicVersionValues { get; } = [];
        public string? LastRequestBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationValues.Add(TryGetHeader(request, "Authorization"));
            ApiKeyValues.Add(TryGetHeader(request, "x-api-key"));
            AnthropicVersionValues.Add(TryGetHeader(request, "anthropic-version"));
            LastRequestBody = request.Content == null
                ? null
                : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(_responseFactory(request));
        }

        private static string? TryGetHeader(HttpRequestMessage request, string name)
        {
            if (!request.Headers.TryGetValues(name, out var values))
                return null;

            return values.FirstOrDefault();
        }
    }

    private sealed class RotatingBearerCredentialProvider : ILlmCredentialProvider
    {
        private readonly Queue<string> _tokens;

        public RotatingBearerCredentialProvider(params string[] tokens)
        {
            _tokens = new Queue<string>(tokens);
        }

        public ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(
            LlmCredentialContext context,
            CancellationToken ct = default)
        {
            var next = _tokens.Count == 0 ? "token-fallback" : _tokens.Dequeue();
            IReadOnlyDictionary<string, string> headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {next}"
            };
            return ValueTask.FromResult(headers);
        }
    }

    private sealed class CaptureRefreshHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public CaptureRefreshHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
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
