using System.Net;
using System.Text;
using Sharp.AI;
using Sharp.AI.Providers;

namespace Sharp.Core.Tests;

public sealed class LlmProviderMappingTests
{
    [Fact]
    public async Task OpenAiProvider_MapsRequestAndAssemblesToolCalls()
    {
        var sse = string.Join(
            "\n",
            [
                "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}",
                "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"function\":{\"name\":\"read\",\"arguments\":\"{\\\"path\\\":\"}}]}}]}",
                "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"README.md\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}",
                "data: [DONE]"
            ]);

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };

        using var provider = new OpenAiLlmProvider(httpClient);

        System.Text.Json.JsonElement? capturedPayload = null;
        var request = new LlmRequest(
            Model: new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools: [ToolDefinition.FromObject("read", "Read file", new
            {
                type = "object",
                properties = new { path = new { type = "string" } },
                required = new[] { "path" }
            })],
            SessionId: "session-123",
            Headers: new Dictionary<string, string> { ["x-test-header"] = "x-value" },
            OnPayload: payload => capturedPayload = payload);

        var events = await CollectAsync(provider.StreamAsync(request));

        Assert.Contains(events, e => e is LlmTextDeltaEvent delta && delta.Delta == "Hi");

        var completed = Assert.IsType<LlmCompletedEvent>(events.Last());
        Assert.Single(completed.ToolCalls);
        Assert.Equal("read", completed.ToolCalls[0].Name);
        Assert.Equal("{\"path\":\"README.md\"}", completed.ToolCalls[0].ArgumentsJson);

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"model\":\"gpt-4o-mini\"", handler.LastRequestBody);
        Assert.Contains("\"tools\"", handler.LastRequestBody);
        Assert.NotNull(capturedPayload);
        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.TryGetValues("x-test-header", out var values));
        Assert.Contains("x-value", values);
        Assert.True(handler.LastRequest.Headers.TryGetValues("x-session-id", out var sessionValues));
        Assert.Contains("session-123", sessionValues);
    }

    [Fact]
    public async Task AnthropicProvider_MapsRequestAndAssemblesThinkingAndToolCalls()
    {
        var sse = string.Join(
            "\n",
            [
                "event: content_block_start",
                "data: {\"index\":0,\"content_block\":{\"type\":\"thinking\"}}",
                "",
                "event: content_block_delta",
                "data: {\"index\":0,\"delta\":{\"type\":\"thinking_delta\",\"thinking\":\"plan\"}}",
                "",
                "event: content_block_delta",
                "data: {\"index\":0,\"delta\":{\"type\":\"signature_delta\",\"signature\":\"thinking-signature\"}}",
                "",
                "event: content_block_stop",
                "data: {\"index\":0}",
                "",
                "event: content_block_start",
                "data: {\"index\":1,\"content_block\":{\"type\":\"tool_use\",\"id\":\"call_1\",\"name\":\"read\",\"signature\":\"tool-signature\"}}",
                "",
                "event: content_block_delta",
                "data: {\"index\":1,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"path\\\":\"}}",
                "",
                "event: content_block_delta",
                "data: {\"index\":1,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"\\\"README.md\\\"}\"}}",
                "",
                "event: content_block_stop",
                "data: {\"index\":1}",
                "",
                "event: content_block_start",
                "data: {\"index\":2,\"content_block\":{\"type\":\"text\"}}",
                "",
                "event: content_block_delta",
                "data: {\"index\":2,\"delta\":{\"type\":\"text_delta\",\"text\":\"done\"}}",
                "",
                "event: content_block_stop",
                "data: {\"index\":2}"
            ]);

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.anthropic.com/v1/")
        };

        using var provider = new AnthropicLlmProvider(httpClient);

        var request = new LlmRequest(
            Model: new ModelDescriptor("anthropic", "claude-sonnet-4-20250514", ProviderApiKind.AnthropicMessages),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools: [ToolDefinition.FromObject("read", "Read file", new
            {
                type = "object",
                properties = new { path = new { type = "string" } },
                required = new[] { "path" }
            })],
            ThinkingLevel: ThinkingLevel.Low);

        var events = await CollectAsync(provider.StreamAsync(request));

        Assert.Contains(events, e => e is LlmThinkingStartedEvent);
        Assert.Contains(events, e => e is LlmThinkingDeltaEvent delta && delta.Delta == "plan");
        Assert.Contains(events, e => e is LlmTextDeltaEvent delta && delta.Delta == "done");

        var completed = Assert.IsType<LlmCompletedEvent>(events.Last());
        Assert.Equal("plan", completed.FullThinking);
        Assert.Equal("thinking-signature", completed.ThinkingSignature);
        Assert.Equal("done", completed.FullText);
        Assert.Single(completed.ToolCalls);
        Assert.Equal("{\"path\":\"README.md\"}", completed.ToolCalls[0].ArgumentsJson);
        Assert.Equal("tool-signature", completed.ToolCalls[0].Signature);

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"thinking\"", handler.LastRequestBody);
        Assert.Contains("\"tools\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task AnthropicProvider_ReplaysAssistantSignaturesInRequestPayload()
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
                "data: {\"index\":0}"
            ]);

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.anthropic.com/v1/")
        };

        using var provider = new AnthropicLlmProvider(httpClient);

        var request = new LlmRequest(
            Model: new ModelDescriptor("anthropic", "claude-sonnet-4-20250514", ProviderApiKind.AnthropicMessages),
            SystemPrompt: "system",
            Messages:
            [
                LlmMessage.UserText("run tool"),
                new LlmMessage(
                    LlmMessageRole.Assistant,
                    [
                        new ThinkingContentBlock("plan", "thinking-sig"),
                        new ToolCallContentBlock("call_1", "bash", "{\"command\":\"pwd\"}", "tool-sig")
                    ]),
                new LlmMessage(
                    LlmMessageRole.Tool,
                    [new ToolResultContentBlock("call_1", "bash", "Exit code: 0\n/tmp\n", false)])
            ],
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.IsType<LlmCompletedEvent>(events.Last());

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"type\":\"thinking\"", handler.LastRequestBody);
        Assert.Contains("\"signature\":\"thinking-sig\"", handler.LastRequestBody);
        Assert.Contains("\"type\":\"tool_use\"", handler.LastRequestBody);
        Assert.Contains("\"signature\":\"tool-sig\"", handler.LastRequestBody);
        Assert.Contains("\"type\":\"tool_result\"", handler.LastRequestBody);
        Assert.Contains("\"tool_use_id\":\"call_1\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task AnthropicProvider_ParsesDataOnlySseWithoutEventHeader()
    {
        var sse = string.Join(
            "\n",
            [
                "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\"}}",
                "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"ok\"}}",
                "data: {\"type\":\"content_block_stop\",\"index\":0}",
                "data: {\"type\":\"message_stop\"}"
            ]);

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.anthropic.com/v1/")
        };

        using var provider = new AnthropicLlmProvider(httpClient);

        var request = new LlmRequest(
            Model: new ModelDescriptor("anthropic", "claude-sonnet-4-20250514", ProviderApiKind.AnthropicMessages),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.Contains(events, e => e is LlmTextDeltaEvent delta && delta.Delta == "ok");
        var completed = Assert.IsType<LlmCompletedEvent>(events.Last());
        Assert.Equal("ok", completed.FullText);
    }

    [Fact]
    public async Task AnthropicProvider_EmptyStream_ReturnsCompatibilityError()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "text/event-stream")
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.anthropic.com/v1/")
        };

        using var provider = new AnthropicLlmProvider(httpClient);

        var request = new LlmRequest(
            Model: new ModelDescriptor("anthropic", "claude-sonnet-4-20250514", ProviderApiKind.AnthropicMessages),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        var error = Assert.IsType<LlmErrorEvent>(Assert.Single(events));
        Assert.Equal(LlmErrorCategory.Validation, error.Category);
        Assert.Contains("no parseable events", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenAiProvider_TooLongRetryAfter_ReturnsRateLimitError()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"error\":\"rate_limited\"}", Encoding.UTF8, "application/json")
            };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return response;
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };

        using var provider = new OpenAiLlmProvider(httpClient);
        var request = new LlmRequest(
            Model: new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools: [],
            MaxRetryDelayMs: 1_000);

        var events = await CollectAsync(provider.StreamAsync(request));
        var error = Assert.IsType<LlmErrorEvent>(events.Single());
        Assert.Equal(LlmErrorCategory.RateLimit, error.Category);
        Assert.True(error.Retryable);
    }

    private static async Task<List<LlmStreamEvent>> CollectAsync(IAsyncEnumerable<LlmStreamEvent> stream)
    {
        var events = new List<LlmStreamEvent>();
        await foreach (var evt in stream)
            events.Add(evt);
        return events;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public string? LastRequestBody { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return _responseFactory(request);
        }
    }
}
