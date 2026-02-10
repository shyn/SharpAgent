using System.Runtime.CompilerServices;
using System.Text.Json;
using Sharp.AI;
using Sharp.Core.Sessions;

namespace Sharp.Core.Tests;

public sealed class AgentSessionControlTests : IDisposable
{
    private readonly string _tempDir;

    public AgentSessionControlTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharpagent-session-control-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task PromptAsync_WithSteeringQueue_InjectsSteeringMessageBetweenTurns()
    {
        var provider = new ScriptedProvider(
            [
                [new LlmCompletedEvent(null, null, [new ToolCall("call_1", "echo", "{\"value\":\"42\"}")])],
                [new LlmCompletedEvent("after-steer", null, [])]
            ]);

        var sessionManager = await SessionManager.CreateAsync(_tempDir, _tempDir);
        var runtime = new ToolRuntime([new EchoTool()], new ToolExecutionContext(_tempDir, sessionManager.SessionId));

        using var session = new AgentSession(
            provider,
            sessionManager,
            runtime,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 5);

        session.Steer("steer-now");

        var events = new List<AgentEvent>();
        await foreach (var evt in session.PromptAsync("run"))
            events.Add(evt);

        Assert.Contains(events, e => e is AgentCompletedEvent);

        var context = sessionManager.RebuildContext();
        Assert.Contains(context, m => m.Role == LlmMessageRole.User
                                      && m.Content.OfType<TextContentBlock>().Any(t => t.Text == "steer-now"));
    }

    [Fact]
    public async Task ContinueAsync_ConsumesFollowUpQueue()
    {
        var provider = new ScriptedProvider(
            [
                [new LlmCompletedEvent("initial", null, [])],
                [new LlmCompletedEvent("follow-up", null, [])]
            ]);

        var sessionManager = await SessionManager.CreateAsync(_tempDir, _tempDir);
        var runtime = new ToolRuntime([new EchoTool()], new ToolExecutionContext(_tempDir, sessionManager.SessionId));

        using var session = new AgentSession(
            provider,
            sessionManager,
            runtime,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 5);

        await foreach (var _ in session.PromptAsync("hello"))
        {
        }

        session.FollowUp("next-step");

        var events = new List<AgentEvent>();
        await foreach (var evt in session.ContinueAsync())
            events.Add(evt);

        Assert.Contains(events, e => e is AgentCompletedEvent);

        var context = sessionManager.RebuildContext();
        Assert.Contains(context, m => m.Role == LlmMessageRole.User
                                      && m.Content.OfType<TextContentBlock>().Any(t => t.Text == "next-step"));
        Assert.Contains(context, m => m.Role == LlmMessageRole.Assistant
                                      && m.Content.OfType<TextContentBlock>().Any(t => t.Text == "follow-up"));
    }

    [Fact]
    public async Task ContinueAsync_WithAssistantTailAndNoQueuedMessages_Throws()
    {
        var provider = new ScriptedProvider(
            [
                [new LlmCompletedEvent("initial", null, [])]
            ]);

        var sessionManager = await SessionManager.CreateAsync(_tempDir, _tempDir);
        var runtime = new ToolRuntime([new EchoTool()], new ToolExecutionContext(_tempDir, sessionManager.SessionId));

        using var session = new AgentSession(
            provider,
            sessionManager,
            runtime,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 5);

        await foreach (var _ in session.PromptAsync("hello"))
        {
        }

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in session.ContinueAsync())
            {
            }
        });
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
                throw new InvalidOperationException("No scripted turns left");

            foreach (var evt in _turns.Dequeue())
                yield return evt;

            await Task.CompletedTask;
        }
    }

    private sealed class EchoTool : IAgentTool
    {
        public string Name => "echo";

        public string Description => "Echoes value";

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
            var value = arguments.TryGetProperty("value", out var prop)
                ? prop.GetString() ?? string.Empty
                : string.Empty;
            return Task.FromResult(ToolInvocationResult.Text($"echo:{value}"));
        }
    }
}
