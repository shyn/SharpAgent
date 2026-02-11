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
        Assert.Equal(LlmStopReason.ToolUse, completed.StopReason);

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"model\":\"gpt-4o-mini\"", handler.LastRequestBody);
        Assert.Contains("\"tools\"", handler.LastRequestBody);
        using (var json = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!))
        {
            var toolFunction = json.RootElement
                .GetProperty("tools")[0]
                .GetProperty("function");
            Assert.True(toolFunction.TryGetProperty("strict", out var strict));
            Assert.False(strict.GetBoolean());
        }
        Assert.NotNull(capturedPayload);
        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.TryGetValues("x-test-header", out var values));
        Assert.Contains("x-value", values);
        Assert.True(handler.LastRequest.Headers.TryGetValues("x-session-id", out var sessionValues));
        Assert.Contains("session-123", sessionValues);
    }

    [Fact]
    public async Task OpenAiProvider_DebugLog_UsesAbsoluteRequestUrl()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("data: [DONE]\n", Encoding.UTF8, "text/event-stream")
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };

        using var provider = new OpenAiLlmProvider(httpClient);

        var logs = new List<string>();
        var request = new LlmRequest(
            Model: new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools: [],
            OnDebugLog: logs.Add);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.IsType<LlmCompletedEvent>(events.Last());
        Assert.Contains(
            logs,
            log => log.StartsWith("request.url=https://api.openai.com/v1/chat/completions", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GoogleAntigravityProvider_MapsRequestAndParsesThinkingTools()
    {
        var sse = string.Join(
            "\n",
            [
                "data: {\"response\":{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"plan\",\"thought\":true,\"thoughtSignature\":\"c2ln\"},{\"text\":\"answer\"},{\"functionCall\":{\"id\":\"call@1\",\"name\":\"read\",\"args\":{\"path\":\"README.md\"}}}]},\"finishReason\":\"STOP\"}],\"usageMetadata\":{\"promptTokenCount\":20,\"cachedContentTokenCount\":5,\"candidatesTokenCount\":6,\"thoughtsTokenCount\":2}}}"
            ]);

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://daily-cloudcode-pa.sandbox.googleapis.com/")
        };

        using var provider = new GoogleAntigravityLlmProvider(httpClient, "proj-42");
        var request = new LlmRequest(
            Model: new ModelDescriptor("google-antigravity", "gemini-3-flash", ProviderApiKind.GoogleGeminiCli),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools:
            [
                ToolDefinition.FromObject("read", "Read file", new
                {
                    type = "object",
                    properties = new { path = new { type = "string" } },
                    required = new[] { "path" }
                })
            ]);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.Contains(events, e => e is LlmThinkingStartedEvent);
        Assert.Contains(events, e => e is LlmThinkingDeltaEvent delta && delta.Delta == "plan");
        Assert.Contains(events, e => e is LlmTextDeltaEvent delta && delta.Delta == "answer");
        Assert.Contains(events, e => e is LlmToolUseStartedEvent start && start.ToolName == "read");

        var completed = Assert.IsType<LlmCompletedEvent>(events.Last());
        Assert.Equal("answer", completed.FullText);
        Assert.Equal("plan", completed.FullThinking);
        Assert.Single(completed.ToolCalls);
        Assert.Equal("{\"path\":\"README.md\"}", completed.ToolCalls[0].ArgumentsJson);
        Assert.Equal(LlmStopReason.ToolUse, completed.StopReason);
        Assert.Equal(15, completed.Usage!.InputTokens);
        Assert.Equal(8, completed.Usage.OutputTokens);
        Assert.Equal(5, completed.Usage.CacheReadTokens);

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"requestType\":\"agent\"", handler.LastRequestBody);
        Assert.Contains("\"userAgent\":\"antigravity\"", handler.LastRequestBody);
        Assert.Contains("\"project\":\"proj-42\"", handler.LastRequestBody);
        Assert.Contains("\"role\":\"user\"", handler.LastRequestBody);
        Assert.Contains("\"functionDeclarations\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task OpenAiProvider_OrphanToolCall_IsBackfilledWithSyntheticToolResult()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("data: [DONE]\n", Encoding.UTF8, "text/event-stream")
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };

        using var provider = new OpenAiLlmProvider(httpClient);

        var request = new LlmRequest(
            Model: new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            SystemPrompt: "system",
            Messages:
            [
                LlmMessage.UserText("run"),
                new LlmMessage(
                    LlmMessageRole.Assistant,
                    [new ToolCallContentBlock("call_1", "read", "{\"path\":\"README.md\"}")]),
                LlmMessage.UserText("continue")
            ],
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.IsType<LlmCompletedEvent>(events.Last());

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"role\":\"tool\"", handler.LastRequestBody);
        Assert.Contains("\"tool_call_id\":\"call_1\"", handler.LastRequestBody);
        Assert.Contains("No result provided", handler.LastRequestBody);
    }

    [Fact]
    public async Task OpenAiProvider_CrossProviderHandoff_NormalizesResponsesToolCallId()
    {
        const string rawToolCallId =
            "call|fc@bad-id-with$chars-and-a-very-very-very-long-suffix-abcdefghijklmnopqrstuvwxyz";

        var payload = await CaptureOpenAiPayloadAsync(
            providerId: "openai",
            baseUrl: "https://api.openai.com/v1/",
            messages:
            [
                LlmMessage.UserText("run"),
                new LlmMessage(
                    LlmMessageRole.Assistant,
                    [
                        new ThinkingContentBlock("plan"),
                        new ToolCallContentBlock(rawToolCallId, "read", "{\"path\":\"README.md\"}")
                    ]),
                new LlmMessage(
                    LlmMessageRole.Tool,
                    [new ToolResultContentBlock(rawToolCallId, "read", "ok", false)]),
                LlmMessage.UserText("continue")
            ]);

        var messages = payload.GetProperty("messages").EnumerateArray().ToArray();
        var assistant = messages.First(item =>
            item.GetProperty("role").GetString() == "assistant"
            && item.TryGetProperty("tool_calls", out var toolCalls)
            && toolCalls.GetArrayLength() > 0);

        var normalizedId = assistant
            .GetProperty("tool_calls")[0]
            .GetProperty("id")
            .GetString();
        Assert.NotNull(normalizedId);
        Assert.DoesNotContain("|", normalizedId!);
        Assert.True(normalizedId!.Length <= 40);
        Assert.All(normalizedId, ch => Assert.True(char.IsLetterOrDigit(ch) || ch is '_' or '-'));

        var toolMessage = messages.First(item => item.GetProperty("role").GetString() == "tool");
        Assert.Equal(normalizedId, toolMessage.GetProperty("tool_call_id").GetString());
        Assert.Contains(
            messages,
            item => item.GetProperty("role").GetString() == "assistant"
                    && item.TryGetProperty("content", out var content)
                    && (content.GetString() ?? string.Empty).Contains("plan", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnthropicProvider_CrossProviderHandoff_NormalizesToolCallIdAndConvertsUnsignedThinkingToText()
    {
        const string rawToolCallId =
            "call|fc@bad-id-with$chars-and-a-very-very-very-long-suffix-abcdefghijklmnopqrstuvwxyz";

        var payload = await CaptureAnthropicPayloadAsync(
            messages:
            [
                LlmMessage.UserText("run"),
                new LlmMessage(
                    LlmMessageRole.Assistant,
                    [
                        new ThinkingContentBlock("plan"),
                        new ToolCallContentBlock(rawToolCallId, "read", "{\"path\":\"README.md\"}")
                    ]),
                new LlmMessage(
                    LlmMessageRole.Tool,
                    [new ToolResultContentBlock(rawToolCallId, "read", "ok", false)]),
                LlmMessage.UserText("continue")
            ]);

        var messages = payload.GetProperty("messages").EnumerateArray().ToArray();
        var assistant = messages.First(item => item.GetProperty("role").GetString() == "assistant");
        var assistantContent = assistant.GetProperty("content").EnumerateArray().ToArray();

        Assert.DoesNotContain(
            assistantContent,
            item => item.GetProperty("type").GetString() == "thinking"
                    && item.TryGetProperty("thinking", out var thinking)
                    && thinking.GetString() == "plan");
        Assert.Contains(
            assistantContent,
            item => item.GetProperty("type").GetString() == "text"
                    && item.GetProperty("text").GetString() == "plan");

        var normalizedId = assistantContent
            .First(item => item.GetProperty("type").GetString() == "tool_use")
            .GetProperty("id")
            .GetString();
        Assert.NotNull(normalizedId);
        Assert.DoesNotContain("|", normalizedId!);
        Assert.True(normalizedId!.Length <= 64);
        Assert.All(normalizedId, ch => Assert.True(char.IsLetterOrDigit(ch) || ch is '_' or '-'));

        var toolResult = messages
            .Where(item => item.GetProperty("role").GetString() == "user")
            .SelectMany(item => item.GetProperty("content").EnumerateArray())
            .First(item => item.GetProperty("type").GetString() == "tool_result");
        Assert.Equal(normalizedId, toolResult.GetProperty("tool_use_id").GetString());
    }

    [Fact]
    public async Task OpenAiProvider_DropsAbortedAssistantTurnBeforeReplay()
    {
        const string callId = "call_abort";

        var payload = await CaptureOpenAiPayloadAsync(
            providerId: "openai",
            baseUrl: "https://api.openai.com/v1/",
            messages:
            [
                LlmMessage.UserText("run"),
                new LlmMessage(
                    LlmMessageRole.Assistant,
                    [new ToolCallContentBlock(callId, "read", "{\"path\":\"README.md\"}")],
                    StopReason: LlmStopReason.Aborted),
                new LlmMessage(
                    LlmMessageRole.Tool,
                    [new ToolResultContentBlock(callId, "read", "stale", false)]),
                LlmMessage.UserText("continue")
            ]);

        var messages = payload.GetProperty("messages").EnumerateArray().ToArray();
        Assert.DoesNotContain(
            messages,
            item => item.GetProperty("role").GetString() == "assistant"
                    && item.TryGetProperty("tool_calls", out var toolCalls)
                    && toolCalls.EnumerateArray().Any(tc => tc.GetProperty("id").GetString() == callId));
        Assert.DoesNotContain(
            messages,
            item => item.GetProperty("role").GetString() == "tool"
                    && item.GetProperty("tool_call_id").GetString() == callId);
    }

    [Fact]
    public async Task AnthropicProvider_DropsAbortedAssistantTurnBeforeReplay()
    {
        const string callId = "call_abort";

        var payload = await CaptureAnthropicPayloadAsync(
            messages:
            [
                LlmMessage.UserText("run"),
                new LlmMessage(
                    LlmMessageRole.Assistant,
                    [new ToolCallContentBlock(callId, "read", "{\"path\":\"README.md\"}")],
                    StopReason: LlmStopReason.Aborted),
                new LlmMessage(
                    LlmMessageRole.Tool,
                    [new ToolResultContentBlock(callId, "read", "stale", false)]),
                LlmMessage.UserText("continue")
            ]);

        var messages = payload.GetProperty("messages").EnumerateArray().ToArray();
        Assert.DoesNotContain(
            messages,
            item => item.GetProperty("role").GetString() == "assistant"
                    && item.GetProperty("content").EnumerateArray().Any(content =>
                        content.GetProperty("type").GetString() == "tool_use"
                        && content.GetProperty("id").GetString() == callId));
        Assert.DoesNotContain(
            messages.Where(item => item.GetProperty("role").GetString() == "user"),
            item => item.GetProperty("content").EnumerateArray().Any(content =>
                content.GetProperty("type").GetString() == "tool_result"
                && content.GetProperty("tool_use_id").GetString() == callId));
    }

    [Fact]
    public async Task OpenAiProvider_DropsLegacyErroredAssistantTurnWithoutStopReason()
    {
        const string callId = "call_legacy_error";

        var payload = await CaptureOpenAiPayloadAsync(
            providerId: "openai",
            baseUrl: "https://api.openai.com/v1/",
            messages:
            [
                LlmMessage.UserText("run"),
                new LlmMessage(
                    LlmMessageRole.Assistant,
                    [new ToolCallContentBlock(callId, "read", "{\"path\":\"README.md\"}")],
                    ErrorMessage: "legacy stream interrupted"),
                new LlmMessage(
                    LlmMessageRole.Tool,
                    [new ToolResultContentBlock(callId, "read", "stale", false)]),
                LlmMessage.UserText("continue")
            ]);

        var messages = payload.GetProperty("messages").EnumerateArray().ToArray();
        Assert.DoesNotContain(
            messages,
            item => item.GetProperty("role").GetString() == "assistant"
                    && item.TryGetProperty("tool_calls", out var toolCalls)
                    && toolCalls.EnumerateArray().Any(tc => tc.GetProperty("id").GetString() == callId));
        Assert.DoesNotContain(
            messages,
            item => item.GetProperty("role").GetString() == "tool"
                    && item.GetProperty("tool_call_id").GetString() == callId);
    }

    [Fact]
    public async Task AnthropicProvider_DropsLegacyErroredAssistantTurnWithoutStopReason()
    {
        const string callId = "call_legacy_error";

        var payload = await CaptureAnthropicPayloadAsync(
            messages:
            [
                LlmMessage.UserText("run"),
                new LlmMessage(
                    LlmMessageRole.Assistant,
                    [new ToolCallContentBlock(callId, "read", "{\"path\":\"README.md\"}")],
                    ErrorMessage: "legacy stream interrupted"),
                new LlmMessage(
                    LlmMessageRole.Tool,
                    [new ToolResultContentBlock(callId, "read", "stale", false)]),
                LlmMessage.UserText("continue")
            ]);

        var messages = payload.GetProperty("messages").EnumerateArray().ToArray();
        Assert.DoesNotContain(
            messages,
            item => item.GetProperty("role").GetString() == "assistant"
                    && item.GetProperty("content").EnumerateArray().Any(content =>
                        content.GetProperty("type").GetString() == "tool_use"
                        && content.GetProperty("id").GetString() == callId));
        Assert.DoesNotContain(
            messages.Where(item => item.GetProperty("role").GetString() == "user"),
            item => item.GetProperty("content").EnumerateArray().Any(content =>
                content.GetProperty("type").GetString() == "tool_result"
                && content.GetProperty("tool_use_id").GetString() == callId));
    }

    [Fact]
    public async Task OpenAiProvider_CompatFlags_ControlRequestPayload()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("data: [DONE]\n", Encoding.UTF8, "text/event-stream")
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };

        using var provider = new OpenAiLlmProvider(httpClient);

        var request = new LlmRequest(
            Model: new ModelDescriptor(
                "openai",
                "gpt-4o-mini",
                ProviderApiKind.OpenAiChatCompletions,
                OpenAiCompletionsCompat: new OpenAiCompletionsCompat(
                    SupportsUsageInStreaming: false,
                    SupportsStrictMode: false,
                    RequiresToolResultName: true,
                    RequiresAssistantAfterToolResult: true,
                    RequiresThinkingAsText: true,
                    MaxTokensField: OpenAiMaxTokensField.MaxCompletionTokens)),
            SystemPrompt: "system",
            Messages:
            [
                LlmMessage.UserText("run"),
                new LlmMessage(
                    LlmMessageRole.Assistant,
                    [
                        new ThinkingContentBlock("plan deeply"),
                        new ToolCallContentBlock("call_1", "read", "{\"path\":\"README.md\"}")
                    ]),
                new LlmMessage(
                    LlmMessageRole.Tool,
                    [new ToolResultContentBlock("call_1", "read", "ok", false)]),
                LlmMessage.UserText("continue")
            ],
            Tools:
            [
                ToolDefinition.FromObject("read", "Read file", new
                {
                    type = "object",
                    properties = new { path = new { type = "string" } },
                    required = new[] { "path" }
                })
            ],
            MaxOutputTokens: 777);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.IsType<LlmCompletedEvent>(events.Last());
        Assert.NotNull(handler.LastRequestBody);

        using var json = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        var root = json.RootElement;

        Assert.False(root.TryGetProperty("stream_options", out _));
        Assert.Equal(777, root.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(root.TryGetProperty("max_tokens", out _));

        var messages = root.GetProperty("messages");
        var assistantRoleCount = messages
            .EnumerateArray()
            .Count(item => item.GetProperty("role").GetString() == "assistant");
        Assert.Equal(2, assistantRoleCount);
        Assert.Contains(
            messages.EnumerateArray(),
            item => item.GetProperty("role").GetString() == "assistant"
                    && item.TryGetProperty("content", out var content)
                    && content.GetString()!.Contains("<thinking>", StringComparison.Ordinal));
        Assert.Contains(
            messages.EnumerateArray(),
            item => item.GetProperty("role").GetString() == "tool"
                    && item.GetProperty("name").GetString() == "read");
        var toolFunction = root.GetProperty("tools")[0].GetProperty("function");
        Assert.False(toolFunction.TryGetProperty("strict", out _));
    }

    [Fact]
    public async Task OpenAiProvider_CompatRequiresMistralToolIds_NormalizesToolCallId()
    {
        var sse = string.Join(
            "\n",
            [
                "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_with_invalid-characters-1234567890\",\"function\":{\"name\":\"read\",\"arguments\":\"{}\"}}]},\"finish_reason\":\"tool_calls\"}]}",
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
        var request = new LlmRequest(
            Model: new ModelDescriptor(
                "openai",
                "gpt-4o-mini",
                ProviderApiKind.OpenAiChatCompletions,
                OpenAiCompletionsCompat: new OpenAiCompletionsCompat(RequiresMistralToolIds: true)),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        var completed = Assert.IsType<LlmCompletedEvent>(events.Last());
        var toolCallId = Assert.Single(completed.ToolCalls).Id;

        Assert.Equal(9, toolCallId.Length);
        Assert.All(toolCallId, ch => Assert.True(char.IsLetterOrDigit(ch)));
    }

    [Fact]
    public async Task OpenAiProvider_CompatSupportsStore_DerivesFromProviderProfile()
    {
        var openAiPayload = await CaptureOpenAiPayloadAsync(
            providerId: "openai",
            baseUrl: "https://api.openai.com/v1/",
            messages: [LlmMessage.UserText("hello")]);
        Assert.True(openAiPayload.TryGetProperty("store", out var store));
        Assert.False(store.GetBoolean());

        var xaiByProviderPayload = await CaptureOpenAiPayloadAsync(
            providerId: "xai",
            baseUrl: "https://api.openai.com/v1/",
            messages: [LlmMessage.UserText("hello")]);
        Assert.False(xaiByProviderPayload.TryGetProperty("store", out _));

        var xaiByBaseUrlPayload = await CaptureOpenAiPayloadAsync(
            providerId: "openai",
            baseUrl: "https://api.x.ai/v1/",
            messages: [LlmMessage.UserText("hello")]);
        Assert.False(xaiByBaseUrlPayload.TryGetProperty("store", out _));
    }

    [Fact]
    public async Task OpenAiProvider_CompatSupportsDeveloperRole_MapsSystemRoleWhenThinkingEnabled()
    {
        var messages = new List<LlmMessage>
        {
            new(LlmMessageRole.System, [new TextContentBlock("policy")]),
            LlmMessage.UserText("hello")
        };

        var openAiPayload = await CaptureOpenAiPayloadAsync(
            providerId: "openai",
            baseUrl: "https://api.openai.com/v1/",
            messages: messages,
            thinkingLevel: ThinkingLevel.Low,
            systemPrompt: null);
        Assert.Contains(
            openAiPayload.GetProperty("messages").EnumerateArray(),
            item => item.GetProperty("role").GetString() == "developer"
                    && item.GetProperty("content").GetString() == "policy");

        var xaiByProviderPayload = await CaptureOpenAiPayloadAsync(
            providerId: "xai",
            baseUrl: "https://api.openai.com/v1/",
            messages: messages,
            thinkingLevel: ThinkingLevel.Low,
            systemPrompt: null);
        Assert.Contains(
            xaiByProviderPayload.GetProperty("messages").EnumerateArray(),
            item => item.GetProperty("role").GetString() == "system"
                    && item.GetProperty("content").GetString() == "policy");

        var xaiByBaseUrlPayload = await CaptureOpenAiPayloadAsync(
            providerId: "openai",
            baseUrl: "https://api.x.ai/v1/",
            messages: messages,
            thinkingLevel: ThinkingLevel.Low,
            systemPrompt: null);
        Assert.Contains(
            xaiByBaseUrlPayload.GetProperty("messages").EnumerateArray(),
            item => item.GetProperty("role").GetString() == "system"
                    && item.GetProperty("content").GetString() == "policy");

        var thinkingOffPayload = await CaptureOpenAiPayloadAsync(
            providerId: "openai",
            baseUrl: "https://api.openai.com/v1/",
            messages: messages,
            thinkingLevel: ThinkingLevel.Off,
            systemPrompt: null);
        Assert.Contains(
            thinkingOffPayload.GetProperty("messages").EnumerateArray(),
            item => item.GetProperty("role").GetString() == "system"
                    && item.GetProperty("content").GetString() == "policy");
    }

    [Fact]
    public async Task OpenAiProvider_CompatSupportsReasoningEffort_UsesOpenAiReasoningEffortWhenSupported()
    {
        var openAiPayload = await CaptureOpenAiPayloadAsync(
            providerId: "openai",
            baseUrl: "https://api.openai.com/v1/",
            messages: [LlmMessage.UserText("hello")],
            thinkingLevel: ThinkingLevel.Medium);
        Assert.Equal("medium", openAiPayload.GetProperty("reasoning_effort").GetString());
        Assert.False(openAiPayload.TryGetProperty("thinking", out _));
        Assert.False(openAiPayload.TryGetProperty("enable_thinking", out _));

        var xaiByProviderPayload = await CaptureOpenAiPayloadAsync(
            providerId: "xai",
            baseUrl: "https://api.openai.com/v1/",
            messages: [LlmMessage.UserText("hello")],
            thinkingLevel: ThinkingLevel.Medium);
        Assert.False(xaiByProviderPayload.TryGetProperty("reasoning_effort", out _));

        var xaiByBaseUrlPayload = await CaptureOpenAiPayloadAsync(
            providerId: "openai",
            baseUrl: "https://api.x.ai/v1/",
            messages: [LlmMessage.UserText("hello")],
            thinkingLevel: ThinkingLevel.Medium);
        Assert.False(xaiByBaseUrlPayload.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public async Task OpenAiProvider_CompatThinkingFormats_DetectFromBaseUrlWhenProviderIsOpenAi()
    {
        var zaiPayload = await CaptureOpenAiPayloadAsync(
            providerId: "openai",
            baseUrl: "https://api.z.ai/v1/",
            messages: [LlmMessage.UserText("hello")],
            thinkingLevel: ThinkingLevel.Low);
        Assert.Equal("enabled", zaiPayload.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(zaiPayload.TryGetProperty("reasoning_effort", out _));
        Assert.False(zaiPayload.TryGetProperty("enable_thinking", out _));

        var qwenPayload = await CaptureOpenAiPayloadAsync(
            providerId: "openai",
            baseUrl: "https://dashscope.aliyuncs.com/compatible-mode/v1/",
            messages: [LlmMessage.UserText("hello")],
            thinkingLevel: ThinkingLevel.Low);
        Assert.True(qwenPayload.GetProperty("enable_thinking").GetBoolean());
        Assert.False(qwenPayload.TryGetProperty("reasoning_effort", out _));
        Assert.False(qwenPayload.TryGetProperty("thinking", out _));
    }

    [Fact]
    public async Task OpenAiProvider_CompatThinkingFormatZai_UsesThinkingType()
    {
        var enabledPayload = await CaptureOpenAiPayloadAsync(
            providerId: "zai",
            baseUrl: "https://api.z.ai/v1/",
            messages: [LlmMessage.UserText("hello")],
            thinkingLevel: ThinkingLevel.Low);
        Assert.Equal("enabled", enabledPayload.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(enabledPayload.TryGetProperty("reasoning_effort", out _));
        Assert.False(enabledPayload.TryGetProperty("enable_thinking", out _));

        var disabledPayload = await CaptureOpenAiPayloadAsync(
            providerId: "zai",
            baseUrl: "https://api.z.ai/v1/",
            messages: [LlmMessage.UserText("hello")],
            thinkingLevel: ThinkingLevel.Off);
        Assert.Equal("disabled", disabledPayload.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Fact]
    public async Task OpenAiProvider_CompatThinkingFormatQwen_UsesEnableThinking()
    {
        var enabledPayload = await CaptureOpenAiPayloadAsync(
            providerId: "qwen",
            baseUrl: "https://dashscope.aliyuncs.com/compatible-mode/v1/",
            messages: [LlmMessage.UserText("hello")],
            thinkingLevel: ThinkingLevel.Low);
        Assert.True(enabledPayload.GetProperty("enable_thinking").GetBoolean());
        Assert.False(enabledPayload.TryGetProperty("reasoning_effort", out _));
        Assert.False(enabledPayload.TryGetProperty("thinking", out _));

        var disabledPayload = await CaptureOpenAiPayloadAsync(
            providerId: "qwen",
            baseUrl: "https://dashscope.aliyuncs.com/compatible-mode/v1/",
            messages: [LlmMessage.UserText("hello")],
            thinkingLevel: ThinkingLevel.Off);
        Assert.False(disabledPayload.GetProperty("enable_thinking").GetBoolean());
    }

    [Fact]
    public async Task OpenAiProvider_CompatRouting_RespectsBaseUrlGates()
    {
        var compat = new OpenAiCompletionsCompat(
            OpenRouterRouting: new OpenAiRoutingPreferences(Order: ["openai", "anthropic"]),
            VercelGatewayRouting: new OpenAiRoutingPreferences(Only: ["openai"]));

        var openRouterPayload = await CaptureOpenAiPayloadAsync(
            providerId: "openai",
            baseUrl: "https://openrouter.ai/api/v1/",
            messages: [LlmMessage.UserText("hello")],
            compat: compat);
        Assert.True(openRouterPayload.TryGetProperty("provider", out var provider));
        var providerOrder = provider
            .GetProperty("order")
            .EnumerateArray()
            .Select(x => x.GetString()!)
            .ToArray();
        Assert.Equal(["openai", "anthropic"], providerOrder);
        Assert.False(openRouterPayload.TryGetProperty("provider_options", out _));

        var vercelPayload = await CaptureOpenAiPayloadAsync(
            providerId: "openai",
            baseUrl: "https://ai-gateway.vercel.sh/v1/",
            messages: [LlmMessage.UserText("hello")],
            compat: compat);
        Assert.False(vercelPayload.TryGetProperty("provider", out _));
        Assert.True(vercelPayload.TryGetProperty("provider_options", out var providerOptions));
        var gatewayOnly = providerOptions
            .GetProperty("gateway")
            .GetProperty("only")
            .EnumerateArray()
            .Select(x => x.GetString()!)
            .ToArray();
        Assert.Equal(["openai"], gatewayOnly);

        var regularPayload = await CaptureOpenAiPayloadAsync(
            providerId: "openrouter",
            baseUrl: "https://api.openai.com/v1/",
            messages: [LlmMessage.UserText("hello")],
            compat: compat);
        Assert.False(regularPayload.TryGetProperty("provider", out _));
        Assert.False(regularPayload.TryGetProperty("provider_options", out _));
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
        Assert.Equal(LlmStopReason.Stop, completed.StopReason);

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"thinking\"", handler.LastRequestBody);
        Assert.Contains("\"tools\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task AnthropicProvider_DebugLog_UsesAbsoluteRequestUrl()
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

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.anthropic.com/v1/")
        };

        using var provider = new AnthropicLlmProvider(httpClient);

        var logs = new List<string>();
        var request = new LlmRequest(
            Model: new ModelDescriptor("anthropic", "claude-sonnet-4-20250514", ProviderApiKind.AnthropicMessages),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools: [],
            OnDebugLog: logs.Add);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.IsType<LlmCompletedEvent>(events.Last());
        Assert.Contains(
            logs,
            log => log.StartsWith("request.url=https://api.anthropic.com/v1/messages", StringComparison.Ordinal));
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
    public async Task AnthropicProvider_ConvertsOpenAiReasoningSignatureToText()
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
                        new ThinkingContentBlock(
                            string.Empty,
                            "{\"type\":\"reasoning\",\"id\":\"rs_1\",\"summary\":[{\"type\":\"summary_text\",\"text\":\"plan\"}]}")
                    ])
            ],
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.IsType<LlmCompletedEvent>(events.Last());

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"type\":\"text\",\"text\":\"plan\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"type\":\"thinking\"", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\":\"rs_1\"", handler.LastRequestBody, StringComparison.Ordinal);
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
        Assert.Equal(LlmStopReason.Stop, completed.StopReason);
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
    public async Task OpenAiProvider_FinishReasonLength_MapsStopReason()
    {
        var sse = string.Join(
            "\n",
            [
                "data: {\"choices\":[{\"delta\":{\"content\":\"truncated\"},\"finish_reason\":\"length\"}]}",
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
        var request = new LlmRequest(
            Model: new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        var completed = Assert.IsType<LlmCompletedEvent>(events.Last());
        Assert.Equal(LlmStopReason.Length, completed.StopReason);
    }

    [Fact]
    public async Task AnthropicProvider_MessageDeltaStopReason_MapsToToolUse()
    {
        var sse = string.Join(
            "\n",
            [
                "event: message_delta",
                "data: {\"delta\":{\"stop_reason\":\"tool_use\"},\"usage\":{\"input_tokens\":10,\"output_tokens\":3}}",
                "",
                "event: content_block_start",
                "data: {\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"call_1\",\"name\":\"read\"}}",
                "",
                "event: content_block_delta",
                "data: {\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"path\\\":\\\"README.md\\\"}\"}}",
                "",
                "event: content_block_stop",
                "data: {\"index\":0}",
                "",
                "event: message_stop",
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
        var completed = Assert.IsType<LlmCompletedEvent>(events.Last());
        Assert.Equal(LlmStopReason.ToolUse, completed.StopReason);
    }

    [Fact]
    public async Task OpenAiResponsesProvider_MapsRequestAndAssemblesThinkingAndToolCalls()
    {
        var sse = string.Join(
            "\n",
            [
                "data: {\"type\":\"response.output_item.added\",\"item\":{\"type\":\"reasoning\",\"id\":\"rs_1\"}}",
                "data: {\"type\":\"response.reasoning_summary_text.delta\",\"delta\":\"plan\"}",
                "data: {\"type\":\"response.output_item.done\",\"item\":{\"type\":\"reasoning\",\"id\":\"rs_1\",\"summary\":[{\"type\":\"summary_text\",\"text\":\"plan\"}]}}",
                "data: {\"type\":\"response.output_item.added\",\"item\":{\"type\":\"function_call\",\"id\":\"fc_1\",\"call_id\":\"call_1\",\"name\":\"read\"}}",
                "data: {\"type\":\"response.function_call_arguments.delta\",\"call_id\":\"call_1\",\"delta\":\"{\\\"path\\\":\\\"README.md\\\"}\"}",
                "data: {\"type\":\"response.output_item.done\",\"item\":{\"type\":\"function_call\",\"id\":\"fc_1\",\"call_id\":\"call_1\",\"name\":\"read\",\"arguments\":\"{\\\"path\\\":\\\"README.md\\\"}\"}}",
                "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"usage\":{\"input_tokens\":9,\"output_tokens\":2,\"input_tokens_details\":{\"cached_tokens\":1}}}}",
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

        using var provider = new OpenAiResponsesLlmProvider(httpClient);
        var request = new LlmRequest(
            Model: new ModelDescriptor("openai", "gpt-5-mini", ProviderApiKind.OpenAiResponses),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools:
            [
                ToolDefinition.FromObject("read", "Read file", new
                {
                    type = "object",
                    properties = new { path = new { type = "string" } },
                    required = new[] { "path" }
                })
            ]);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.Contains(events, e => e is LlmThinkingStartedEvent);
        Assert.Contains(events, e => e is LlmThinkingDeltaEvent delta && delta.Delta == "plan");

        var completed = Assert.IsType<LlmCompletedEvent>(events.Last());
        Assert.Single(completed.ToolCalls);
        Assert.Equal("read", completed.ToolCalls[0].Name);
        Assert.Equal("{\"path\":\"README.md\"}", completed.ToolCalls[0].ArgumentsJson);
        Assert.Equal(LlmStopReason.ToolUse, completed.StopReason);
        Assert.NotNull(completed.Usage);
        Assert.Equal(9, completed.Usage!.InputTokens);
        Assert.Equal(1, completed.Usage.CacheReadTokens);

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"model\":\"gpt-5-mini\"", handler.LastRequestBody);
        Assert.Contains("\"input\"", handler.LastRequestBody);
        Assert.Contains("\"tools\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task OpenAiResponsesProvider_OrphanToolCall_IsBackfilledWithFunctionCallOutput()
    {
        var sse = string.Join(
            "\n",
            [
                "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}",
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

        using var provider = new OpenAiResponsesLlmProvider(httpClient);
        var request = new LlmRequest(
            Model: new ModelDescriptor("openai", "gpt-5-mini", ProviderApiKind.OpenAiResponses),
            SystemPrompt: "system",
            Messages:
            [
                LlmMessage.UserText("run"),
                new LlmMessage(
                    LlmMessageRole.Assistant,
                    [new ToolCallContentBlock("call_1", "read", "{\"path\":\"README.md\"}")]),
                LlmMessage.UserText("continue")
            ],
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.IsType<LlmCompletedEvent>(events.Last());

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"type\":\"function_call_output\"", handler.LastRequestBody);
        Assert.Contains("\"call_id\":\"call_1\"", handler.LastRequestBody);
        Assert.Contains("No result provided", handler.LastRequestBody);
    }

    [Fact]
    public async Task OpenAiResponsesProvider_ReplaysThinkingSignatureAsReasoningItem()
    {
        var sse = string.Join(
            "\n",
            [
                "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}",
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

        using var provider = new OpenAiResponsesLlmProvider(httpClient);
        var request = new LlmRequest(
            Model: new ModelDescriptor("openai", "gpt-5-mini", ProviderApiKind.OpenAiResponses),
            SystemPrompt: "system",
            Messages:
            [
                LlmMessage.UserText("run"),
                new LlmMessage(
                    LlmMessageRole.Assistant,
                    [
                        new ThinkingContentBlock(
                            string.Empty,
                            "{\"type\":\"reasoning\",\"id\":\"rs_1\",\"summary\":[{\"type\":\"summary_text\",\"text\":\"plan\"}]}"),
                        new ToolCallContentBlock("call_1", "read", "{\"path\":\"README.md\"}")
                    ]),
                new LlmMessage(
                    LlmMessageRole.Tool,
                    [new ToolResultContentBlock("call_1", "read", "ok", false)]),
                LlmMessage.UserText("continue")
            ],
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.IsType<LlmCompletedEvent>(events.Last());

        Assert.NotNull(handler.LastRequestBody);
        using var json = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        var input = json.RootElement.GetProperty("input").EnumerateArray().ToArray();

        Assert.Contains(
            input,
            item => item.TryGetProperty("type", out var type) && type.GetString() == "reasoning");
        Assert.Contains(
            input,
            item => item.TryGetProperty("type", out var type) && type.GetString() == "function_call");
        Assert.Contains(
            input,
            item => item.TryGetProperty("type", out var type) && type.GetString() == "function_call_output");
    }

    [Fact]
    public async Task OpenAiResponsesProvider_NormalizesWrappedReasoningSignature()
    {
        var sse = string.Join(
            "\n",
            [
                "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}",
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

        using var provider = new OpenAiResponsesLlmProvider(httpClient);
        var request = new LlmRequest(
            Model: new ModelDescriptor("openai", "gpt-5-mini", ProviderApiKind.OpenAiResponses),
            SystemPrompt: "system",
            Messages:
            [
                LlmMessage.UserText("run"),
                new LlmMessage(
                    LlmMessageRole.Assistant,
                    [
                        new ThinkingContentBlock(
                            string.Empty,
                            "{\"reasoning\":{\"id\":\"rs_wrap\",\"summary\":[{\"type\":\"summary_text\",\"text\":\"plan\"}]}}"),
                        new ToolCallContentBlock("call_1", "read", "{\"path\":\"README.md\"}")
                    ]),
                new LlmMessage(
                    LlmMessageRole.Tool,
                    [new ToolResultContentBlock("call_1", "read", "ok", false)]),
                LlmMessage.UserText("continue")
            ],
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.IsType<LlmCompletedEvent>(events.Last());

        Assert.NotNull(handler.LastRequestBody);
        using var json = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        var input = json.RootElement.GetProperty("input").EnumerateArray().ToArray();

        var reasoning = input.Single(item =>
            item.TryGetProperty("type", out var type) && type.GetString() == "reasoning");
        Assert.Equal("rs_wrap", reasoning.GetProperty("id").GetString());
    }

    [Fact]
    public async Task OpenAiResponsesProvider_SkipsSignatureOnlyAssistantTurn()
    {
        var sse = string.Join(
            "\n",
            [
                "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}",
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

        using var provider = new OpenAiResponsesLlmProvider(httpClient);
        var request = new LlmRequest(
            Model: new ModelDescriptor("openai", "gpt-5-mini", ProviderApiKind.OpenAiResponses),
            SystemPrompt: "system",
            Messages:
            [
                LlmMessage.UserText("first"),
                new LlmMessage(
                    LlmMessageRole.Assistant,
                    [
                        new ThinkingContentBlock(
                            string.Empty,
                            "{\"type\":\"reasoning\",\"id\":\"rs_partial\"}")
                    ]),
                LlmMessage.UserText("second")
            ],
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.IsType<LlmCompletedEvent>(events.Last());

        Assert.NotNull(handler.LastRequestBody);
        using var json = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        var input = json.RootElement.GetProperty("input").EnumerateArray().ToArray();

        Assert.Equal(3, input.Length); // system + two user messages
        Assert.DoesNotContain(
            input,
            item => item.TryGetProperty("type", out var type) && type.GetString() == "reasoning");
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

    [Fact]
    public async Task OpenAiProvider_ContextOverflowBody_MapsContextOverflowError()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"error\":{\"message\":\"This model's maximum context length is 8192 tokens.\"}}",
                Encoding.UTF8,
                "application/json")
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
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        var error = Assert.IsType<LlmErrorEvent>(events.Single());
        Assert.Equal(LlmErrorCategory.ContextOverflow, error.Category);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task OpenAiResponsesProvider_ContextOverflowBody_MapsContextOverflowError()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"error\":{\"message\":\"Prompt is too long for the model context window.\"}}",
                Encoding.UTF8,
                "application/json")
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };

        using var provider = new OpenAiResponsesLlmProvider(httpClient);
        var request = new LlmRequest(
            Model: new ModelDescriptor("openai", "gpt-5-mini", ProviderApiKind.OpenAiResponses),
            SystemPrompt: "system",
            Messages: [LlmMessage.UserText("hello")],
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        var error = Assert.IsType<LlmErrorEvent>(events.Single());
        Assert.Equal(LlmErrorCategory.ContextOverflow, error.Category);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task AnthropicProvider_ContextOverflowBody_MapsContextOverflowError()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"error\":{\"message\":\"Prompt is too long: please reduce the length.\"}}",
                Encoding.UTF8,
                "application/json")
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
        var error = Assert.IsType<LlmErrorEvent>(events.Single());
        Assert.Equal(LlmErrorCategory.ContextOverflow, error.Category);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task OpenAiProvider_RetryableStatus_RetriesAndSucceeds()
    {
        var callCount = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                var retry = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("{\"error\":\"rate_limited\"}", Encoding.UTF8, "application/json")
                };
                retry.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return retry;
            }

            var sse = string.Join(
                "\n",
                [
                    "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}",
                    "data: [DONE]"
                ]);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            };
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
            MaxRetryDelayMs: 10);

        var events = await CollectAsync(provider.StreamAsync(request));
        var completed = Assert.IsType<LlmCompletedEvent>(events.Last());
        Assert.Equal("ok", completed.FullText);
        Assert.Equal(2, callCount);
    }

    private static async Task<System.Text.Json.JsonElement> CaptureOpenAiPayloadAsync(
        string providerId,
        string baseUrl,
        IReadOnlyList<LlmMessage> messages,
        ThinkingLevel thinkingLevel = ThinkingLevel.Off,
        string? systemPrompt = "system",
        OpenAiCompletionsCompat? compat = null)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("data: [DONE]\n", Encoding.UTF8, "text/event-stream")
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl)
        };

        using var provider = new OpenAiLlmProvider(httpClient);
        var request = new LlmRequest(
            Model: new ModelDescriptor(
                providerId,
                "gpt-4o-mini",
                ProviderApiKind.OpenAiChatCompletions,
                OpenAiCompletionsCompat: compat),
            SystemPrompt: systemPrompt,
            Messages: messages,
            Tools: [],
            ThinkingLevel: thinkingLevel);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.IsType<LlmCompletedEvent>(events.Last());
        Assert.NotNull(handler.LastRequestBody);

        using var json = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        return json.RootElement.Clone();
    }

    private static async Task<System.Text.Json.JsonElement> CaptureAnthropicPayloadAsync(
        IReadOnlyList<LlmMessage> messages,
        string? systemPrompt = "system")
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
            SystemPrompt: systemPrompt,
            Messages: messages,
            Tools: []);

        var events = await CollectAsync(provider.StreamAsync(request));
        Assert.IsType<LlmCompletedEvent>(events.Last());
        Assert.NotNull(handler.LastRequestBody);

        using var json = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        return json.RootElement.Clone();
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
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequest = request;
            LastRequestBody = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return _responseFactory(request);
        }
    }
}
