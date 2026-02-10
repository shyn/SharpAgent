using System.Runtime.CompilerServices;
using System.Text.Json;
using Sharp.AI;
using Sharp.Core;
using Sharp.Core.Sessions;

namespace Sharp.Core.Tests;

public sealed class AgentSessionIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public AgentSessionIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharpagent-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task PromptAsync_PersistsUserAssistantToolAssistantFlow()
    {
        var provider = new ScriptedProvider(
            [
                [new LlmCompletedEvent(null, null, [new ToolCall("call_1", "echo", "{\"value\":\"42\"}")])],
                [new LlmCompletedEvent("final", null, [])]
            ]);

        var sessionManager = await SessionManager.CreateAsync(_tempDir, _tempDir);
        var runtime = new ToolRuntime([new EchoTool()]);

        using var session = new AgentSession(
            provider,
            sessionManager,
            runtime,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 5);

        var events = new List<AgentEvent>();
        await foreach (var evt in session.PromptAsync("calculate"))
            events.Add(evt);

        Assert.Contains(events, e => e is AgentCompletedEvent);

        var reloaded = await SessionManager.LoadAsync(sessionManager.SessionFilePath);
        var context = reloaded.RebuildContext();

        Assert.Equal(4, context.Count);
        Assert.Equal(LlmMessageRole.User, context[0].Role);
        Assert.Equal(LlmMessageRole.Assistant, context[1].Role);
        Assert.Equal(LlmMessageRole.Tool, context[2].Role);
        Assert.Equal(LlmMessageRole.Assistant, context[3].Role);
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
