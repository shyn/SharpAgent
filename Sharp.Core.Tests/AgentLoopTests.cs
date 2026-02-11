using System.Runtime.CompilerServices;
using System.Text.Json;
using Sharp.AI;
using Sharp.Core;

namespace Sharp.Core.Tests;

public sealed class AgentLoopTests
{
    [Fact]
    public async Task RunAsync_NoToolCalls_CompletesWithAssistantMessage()
    {
        var provider = new ScriptedProvider(
            [
                [
                    new LlmTextDeltaEvent("hello"),
                    new LlmCompletedEvent("hello", null, [])
                ]
            ]);

        var tools = new ToolRuntime([]);
        var loop = new AgentLoop(provider, tools);

        var conversation = new List<LlmMessage>
        {
            LlmMessage.UserText("say hello")
        };

        var persisted = new List<LlmMessage>();
        var events = new List<AgentEvent>();

        await foreach (var evt in loop.RunAsync(
                           conversation,
                           "say hello",
                           new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
                           "system",
                           ThinkingLevel.Off,
                           5,
                           (message, _) =>
                           {
                               persisted.Add(message);
                               return Task.CompletedTask;
                           }))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is AgentCompletedEvent);
        Assert.Single(persisted);
        Assert.Equal(LlmMessageRole.Assistant, persisted[0].Role);
        Assert.Equal("hello", ((TextContentBlock)persisted[0].Content[0]).Text);
    }

    [Fact]
    public async Task RunAsync_WithToolCall_ExecutesToolAndContinues()
    {
        var provider = new ScriptedProvider(
            [
                [
                    new LlmToolUseStartedEvent("call_1", "echo"),
                    new LlmToolUseArgumentsDeltaEvent("call_1", "{\"value\":\"42\"}"),
                    new LlmToolUseCompletedEvent("call_1"),
                    new LlmCompletedEvent(null, null, [new ToolCall("call_1", "echo", "{\"value\":\"42\"}", "sig-1")])
                ],
                [
                    new LlmTextDeltaEvent("done"),
                    new LlmCompletedEvent("done", null, [])
                ]
            ]);

        var runtime = new ToolRuntime([new EchoTool()]);
        var loop = new AgentLoop(provider, runtime);

        var conversation = new List<LlmMessage>
        {
            LlmMessage.UserText("run tool")
        };

        var persisted = new List<LlmMessage>();
        var events = new List<AgentEvent>();

        await foreach (var evt in loop.RunAsync(
                           conversation,
                           "run tool",
                           new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
                           "system",
                           ThinkingLevel.Off,
                           5,
                           (message, _) =>
                           {
                               persisted.Add(message);
                               return Task.CompletedTask;
                           }))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is AgentToolExecutionStartedEvent started && started.ToolName == "echo");
        Assert.Contains(events, e => e is AgentToolExecutionCompletedEvent completed && !completed.Result.IsError);
        Assert.Contains(events, e => e is AgentCompletedEvent);

        Assert.Equal(3, persisted.Count);
        Assert.Equal(LlmMessageRole.Assistant, persisted[0].Role);
        Assert.Equal(LlmMessageRole.Tool, persisted[1].Role);
        Assert.Equal(LlmMessageRole.Assistant, persisted[2].Role);

        var toolCallBlock = Assert.IsType<ToolCallContentBlock>(persisted[0].Content[0]);
        Assert.Equal("sig-1", toolCallBlock.Signature);

        var toolBlock = Assert.IsType<ToolResultContentBlock>(persisted[1].Content[0]);
        Assert.Equal("echo:42", toolBlock.ContentText);
    }

    [Fact]
    public async Task RunAsync_OnProviderError_PersistsErrorAssistantMessage()
    {
        var provider = new ScriptedProvider(
            [
                [
                    new LlmThinkingDeltaEvent("plan"),
                    new LlmTextDeltaEvent("partial"),
                    new LlmErrorEvent("boom", LlmErrorCategory.Validation)
                ]
            ]);

        var loop = new AgentLoop(provider, new ToolRuntime([]));
        var conversation = new List<LlmMessage>
        {
            LlmMessage.UserText("trigger")
        };

        var persisted = new List<LlmMessage>();
        var events = new List<AgentEvent>();

        await foreach (var evt in loop.RunAsync(
                           conversation,
                           "trigger",
                           new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
                           "system",
                           ThinkingLevel.Off,
                           5,
                           (message, _) =>
                           {
                               persisted.Add(message);
                               return Task.CompletedTask;
                           }))
        {
            events.Add(evt);
        }

        var error = Assert.IsType<AgentErrorEvent>(events.Last());
        Assert.Equal("boom", error.Message);
        Assert.Single(persisted);

        var assistant = persisted[0];
        Assert.Equal(LlmMessageRole.Assistant, assistant.Role);
        Assert.Equal(LlmStopReason.Error, assistant.StopReason);
        Assert.Equal("boom", assistant.ErrorMessage);
        Assert.Contains(assistant.Content, block => block is ThinkingContentBlock thinking && thinking.Text == "plan");
        Assert.Contains(assistant.Content, block => block is TextContentBlock text && text.Text == "partial");
    }

    [Fact]
    public async Task RunAsync_OnProviderAbort_PersistsAbortedAssistantMessage()
    {
        var provider = new ScriptedProvider(
            [
                [
                    new LlmTextDeltaEvent("partial"),
                    new LlmErrorEvent("aborted", LlmErrorCategory.Aborted)
                ]
            ]);

        var loop = new AgentLoop(provider, new ToolRuntime([]));
        var conversation = new List<LlmMessage>
        {
            LlmMessage.UserText("trigger")
        };

        var persisted = new List<LlmMessage>();
        await foreach (var _ in loop.RunAsync(
                           conversation,
                           "trigger",
                           new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
                           "system",
                           ThinkingLevel.Off,
                           5,
                           (message, _) =>
                           {
                               persisted.Add(message);
                               return Task.CompletedTask;
                           }))
        {
        }

        Assert.Single(persisted);
        Assert.Equal(LlmStopReason.Aborted, persisted[0].StopReason);
        Assert.Equal("aborted", persisted[0].ErrorMessage);
    }

    private sealed class ScriptedProvider : ILlmProvider
    {
        private readonly Queue<IReadOnlyList<LlmStreamEvent>> _turns;

        public ScriptedProvider(IEnumerable<IReadOnlyList<LlmStreamEvent>> turns)
        {
            _turns = new Queue<IReadOnlyList<LlmStreamEvent>>(turns);
        }

        public string ProviderId => "scripted";

        public void Dispose()
        {
        }

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (_turns.Count == 0)
                throw new InvalidOperationException("No scripted turn is available");

            foreach (var evt in _turns.Dequeue())
            {
                yield return evt;
            }

            await Task.CompletedTask;
        }
    }

    private sealed class EchoTool : IAgentTool
    {
        public string Name => "echo";

        public string Description => "Echoes input";

        public JsonElement ParametersSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { value = new { type = "string" } },
            required = new[] { "value" }
        }, JsonDefaults.Options);

        public Task<ToolInvocationResult> ExecuteAsync(
            JsonElement arguments,
            ToolExecutionContext context,
            IProgress<ToolInvocationResult>? progress = null,
            CancellationToken ct = default)
        {
            var value = arguments.TryGetProperty("value", out var valueProp)
                ? valueProp.GetString() ?? string.Empty
                : string.Empty;
            return Task.FromResult(ToolInvocationResult.Text($"echo:{value}"));
        }
    }
}
