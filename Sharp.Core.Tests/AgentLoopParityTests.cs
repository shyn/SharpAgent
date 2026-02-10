using Sharp.AI;
using Sharp.Core.Tests.TestDoubles;

namespace Sharp.Core.Tests;

public sealed class AgentLoopParityTests
{
    [Fact]
    public async Task RunControlledAsync_AppliesTransformBeforeConvert()
    {
        var provider = new RecordingProvider();
        provider.Enqueue(new LlmCompletedEvent("ok", null, []));

        var loop = new AgentLoop(provider, new ToolRuntime([]));
        var conversation = new List<LlmMessage> { LlmMessage.UserText("original") };
        var persisted = new List<LlmMessage>();
        var order = new List<string>();

        async Task<IReadOnlyList<LlmMessage>> Transform(IReadOnlyList<LlmMessage> input, CancellationToken ct)
        {
            await Task.CompletedTask;
            order.Add("transform");
            Assert.Single(input);
            return [LlmMessage.UserText("transformed")];
        }

        async Task<IReadOnlyList<LlmMessage>> Convert(IReadOnlyList<LlmMessage> input, CancellationToken ct)
        {
            await Task.CompletedTask;
            order.Add("convert");
            Assert.Single(input);
            Assert.Equal("transformed", ((TextContentBlock)input[0].Content[0]).Text);
            return input;
        }

        var events = new List<AgentEvent>();
        await foreach (var evt in loop.RunControlledAsync(
                           conversation,
                           prompt: "run",
                           isContinuation: false,
                           new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
                           "system",
                           ThinkingLevel.Off,
                           maxTurns: 3,
                           appendMessage: (m, _) =>
                           {
                               persisted.Add(m);
                               return Task.CompletedTask;
                           },
                           transformContext: Transform,
                           convertToLlm: Convert))
        {
            events.Add(evt);
        }

        Assert.Equal(["transform", "convert"], order);
        Assert.Single(provider.Requests);
        Assert.Single(provider.Requests[0].Messages);
        Assert.Equal("transformed", ((TextContentBlock)provider.Requests[0].Messages[0].Content[0]).Text);
        Assert.Contains(events, e => e is AgentCompletedEvent);
        Assert.Single(persisted);
    }

    [Fact]
    public async Task RunControlledAsync_WhenTransformThrows_EmitsValidationError()
    {
        var provider = new RecordingProvider();
        var loop = new AgentLoop(provider, new ToolRuntime([]));

        var events = new List<AgentEvent>();
        await foreach (var evt in loop.RunControlledAsync(
                           conversation: [LlmMessage.UserText("hello")],
                           prompt: "hello",
                           isContinuation: false,
                           new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
                           "system",
                           ThinkingLevel.Off,
                           maxTurns: 3,
                           appendMessage: (_, _) => Task.CompletedTask,
                           transformContext: (_, _) => throw new InvalidOperationException("boom")))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is AgentErrorEvent error
                                     && error.Category == LlmErrorCategory.Validation
                                     && error.Message.Contains("Context transform failed", StringComparison.Ordinal));
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task RunControlledAsync_WithToolPartialUpdates_EmitsUpdateEvents()
    {
        var provider = new RecordingProvider();
        provider.Enqueue(new LlmCompletedEvent(null, null, [new ToolCall("call_1", "partial_tool", "{\"value\":\"42\"}")]));
        provider.Enqueue(new LlmCompletedEvent("done", null, []));

        var runtime = new ToolRuntime([new PartialUpdateTool()]);
        var loop = new AgentLoop(provider, runtime);

        var events = new List<AgentEvent>();
        await foreach (var evt in loop.RunControlledAsync(
                           conversation: [LlmMessage.UserText("run tool")],
                           prompt: "run tool",
                           isContinuation: false,
                           new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
                           "system",
                           ThinkingLevel.Off,
                           maxTurns: 5,
                           appendMessage: (_, _) => Task.CompletedTask))
        {
            events.Add(evt);
        }

        var updates = events
            .OfType<AgentToolExecutionUpdatedEvent>()
            .Where(x => x.ToolCallId == "call_1")
            .ToList();

        Assert.True(updates.Count >= 2);
        Assert.Contains(updates, x => x.PartialResult.ContentAsText.Contains("partial-1:42", StringComparison.Ordinal));
        Assert.Contains(events, e => e is AgentCompletedEvent);
    }

    [Fact]
    public async Task RunControlledAsync_WhenSteeringQueued_InjectsMessageBeforeNextTurn()
    {
        var provider = new RecordingProvider();
        provider.Enqueue(new LlmCompletedEvent(
            null,
            null,
            [
                new ToolCall("call_1", "echo", "{\"value\":\"first\"}"),
                new ToolCall("call_2", "echo", "{\"value\":\"second\"}")
            ]));
        provider.Enqueue(new LlmCompletedEvent("after-steer", null, []));

        var runtime = new ToolRuntime([new EchoTool()]);
        var loop = new AgentLoop(provider, runtime);
        var persisted = new List<LlmMessage>();

        var steeringReturned = false;
        Task<IReadOnlyList<LlmMessage>> DequeueSteering(CancellationToken _)
        {
            if (steeringReturned)
                return Task.FromResult<IReadOnlyList<LlmMessage>>(Array.Empty<LlmMessage>());

            steeringReturned = true;
            return Task.FromResult<IReadOnlyList<LlmMessage>>([LlmMessage.UserText("interrupt")]);
        }

        var events = new List<AgentEvent>();
        await foreach (var evt in loop.RunControlledAsync(
                           conversation: [LlmMessage.UserText("start")],
                           prompt: "start",
                           isContinuation: false,
                           new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
                           "system",
                           ThinkingLevel.Off,
                           maxTurns: 5,
                           appendMessage: (message, _) =>
                           {
                               persisted.Add(message);
                               return Task.CompletedTask;
                           },
                           dequeueSteeringMessages: DequeueSteering))
        {
            events.Add(evt);
        }

        Assert.Equal(
            [LlmMessageRole.Assistant, LlmMessageRole.Tool, LlmMessageRole.User, LlmMessageRole.Assistant],
            persisted.Select(x => x.Role).ToList());

        var turns = events.OfType<AgentTurnCompletedEvent>().ToList();
        Assert.Single(turns);
        Assert.Single(turns[0].ToolMessages);
        Assert.Contains(events, e => e is AgentCompletedEvent);
    }

    [Fact(Skip = "Pending parity with pi-agent: skipped tool results for interrupted tool queue")]
    public async Task RunControlledAsync_WhenSteeringInterrupts_RemainingToolCallsShouldBeSkipped()
    {
        // Reference parity target:
        // /Users/deepwind/repo/SharpAgent/pi-mono/packages/agent/test/agent-loop.test.ts
        await Task.CompletedTask;
    }
}
