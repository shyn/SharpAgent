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
}
