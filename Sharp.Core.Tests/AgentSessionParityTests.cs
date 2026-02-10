using System.Runtime.CompilerServices;
using System.Text.Json;
using Sharp.AI;
using Sharp.Core.Sessions;
using Sharp.Core.Tests.TestDoubles;

namespace Sharp.Core.Tests;

public sealed class AgentSessionParityTests : IDisposable
{
    private readonly string _tempDir;

    public AgentSessionParityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharpagent-session-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task PromptAsync_WhenStreaming_Throws()
    {
        var provider = new BlockingProvider();
        var sessionManager = await SessionManager.CreateAsync(_tempDir, _tempDir);
        var runtime = new ToolRuntime([]);

        using var session = CreateSession(provider, sessionManager, runtime);

        var background = ConsumeAsync(session.PromptAsync("first"));
        await provider.WaitForStartedAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in session.PromptAsync("second"))
            {
            }
        });

        Assert.Contains("Session is already running", exception.Message, StringComparison.Ordinal);

        session.Abort();
        await session.WaitForIdleAsync();
        await background;
    }

    [Fact]
    public async Task ContinueAsync_WhenStreaming_Throws()
    {
        var provider = new BlockingProvider();
        var sessionManager = await SessionManager.CreateAsync(_tempDir, _tempDir);
        var runtime = new ToolRuntime([]);

        using var session = CreateSession(provider, sessionManager, runtime);

        var background = ConsumeAsync(session.PromptAsync("first"));
        await provider.WaitForStartedAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in session.ContinueAsync())
            {
            }
        });

        Assert.Contains("Session is already running", exception.Message, StringComparison.Ordinal);

        session.Abort();
        await session.WaitForIdleAsync();
        await background;
    }

    [Fact]
    public async Task ContinueAsync_WithAssistantTail_ConsumesSteeringBeforeFollowUp()
    {
        var provider = new RecordingProvider();
        provider.Enqueue(new LlmCompletedEvent("initial", null, []));
        provider.Enqueue(new LlmCompletedEvent("after-steer", null, []));
        provider.Enqueue(new LlmCompletedEvent("after-follow-up", null, []));

        var sessionManager = await SessionManager.CreateAsync(_tempDir, _tempDir);
        var runtime = new ToolRuntime([]);

        using var session = CreateSession(provider, sessionManager, runtime);

        await ConsumeAsync(session.PromptAsync("hello"));

        session.Steer("steer-first");
        session.FollowUp("follow-second");

        await ConsumeAsync(session.ContinueAsync());

        var context = sessionManager.RebuildContext();
        var userTexts = context
            .Where(m => m.Role == LlmMessageRole.User)
            .Select(m => m.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text)
            .Where(text => text != null)
            .ToList();

        Assert.Equal(["hello", "steer-first", "follow-second"], userTexts);

        var assistantTexts = context
            .Where(m => m.Role == LlmMessageRole.Assistant)
            .Select(m => m.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text)
            .Where(text => text != null)
            .ToList();

        Assert.Equal(["initial", "after-steer", "after-follow-up"], assistantTexts);
    }

    [Fact]
    public async Task ContinueAsync_WithOneAtATimeMode_DequeuesOneSteeringPerRun()
    {
        var provider = new RecordingProvider();
        provider.Enqueue(new LlmCompletedEvent("initial", null, []));
        provider.Enqueue(new LlmCompletedEvent("after-first-steer", null, []));
        provider.Enqueue(new LlmCompletedEvent("after-second-steer", null, []));

        var sessionManager = await SessionManager.CreateAsync(_tempDir, _tempDir);
        var runtime = new ToolRuntime([]);

        using var session = CreateSession(provider, sessionManager, runtime);
        session.SteeringMode = QueueDeliveryMode.OneAtATime;

        await ConsumeAsync(session.PromptAsync("hello"));

        session.Steer("steer-1");
        session.Steer("steer-2");

        await ConsumeAsync(session.ContinueAsync());

        var firstRunUsers = sessionManager.RebuildContext()
            .Where(m => m.Role == LlmMessageRole.User)
            .Select(m => m.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text)
            .Where(text => text != null)
            .ToList();

        Assert.Equal(["hello", "steer-1"], firstRunUsers);

        await ConsumeAsync(session.ContinueAsync());

        var secondRunUsers = sessionManager.RebuildContext()
            .Where(m => m.Role == LlmMessageRole.User)
            .Select(m => m.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text)
            .Where(text => text != null)
            .ToList();

        Assert.Equal(["hello", "steer-1", "steer-2"], secondRunUsers);
    }

    [Fact]
    public async Task Abort_AndWaitForIdleAsync_EndStreamingRun()
    {
        var provider = new BlockingProvider();
        var sessionManager = await SessionManager.CreateAsync(_tempDir, _tempDir);
        var runtime = new ToolRuntime([]);

        using var session = CreateSession(provider, sessionManager, runtime);

        var eventsTask = CollectAsync(session.PromptAsync("hello"));
        await provider.WaitForStartedAsync();

        session.Abort();
        await session.WaitForIdleAsync();
        var events = await eventsTask;

        Assert.False(session.IsStreaming);
        Assert.Contains(events, e => e is AgentErrorEvent error && error.Category == LlmErrorCategory.Aborted);
    }

    [Fact]
    public async Task ContinueAsync_OnEmptySession_Throws()
    {
        var provider = new RecordingProvider();
        var sessionManager = await SessionManager.CreateAsync(_tempDir, _tempDir);
        var runtime = new ToolRuntime([]);

        using var session = CreateSession(provider, sessionManager, runtime);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in session.ContinueAsync())
            {
            }
        });

        Assert.Equal("Cannot continue from an empty session", exception.Message);
    }

    [Fact]
    public async Task SteerAndFollowUp_AreAllowedWhileStreaming()
    {
        var provider = new BlockingProvider();
        var sessionManager = await SessionManager.CreateAsync(_tempDir, _tempDir);
        var runtime = new ToolRuntime([]);

        using var session = CreateSession(provider, sessionManager, runtime);
        var background = ConsumeAsync(session.PromptAsync("hello"));
        await provider.WaitForStartedAsync();

        var steerException = Record.Exception(() => session.Steer("queued-steer"));
        var followUpException = Record.Exception(() => session.FollowUp("queued-follow-up"));

        Assert.Null(steerException);
        Assert.Null(followUpException);

        session.Abort();
        await session.WaitForIdleAsync();
        await background;
    }

    [Fact]
    public async Task PromptAsync_AfterPreviousCompletion_AllowsNextPrompt()
    {
        var provider = new RecordingProvider();
        provider.Enqueue(new LlmCompletedEvent("a1", null, []));
        provider.Enqueue(new LlmCompletedEvent("a2", null, []));

        var sessionManager = await SessionManager.CreateAsync(_tempDir, _tempDir);
        var runtime = new ToolRuntime([]);
        using var session = CreateSession(provider, sessionManager, runtime);

        await ConsumeAsync(session.PromptAsync("u1"));
        await session.WaitForIdleAsync();
        await ConsumeAsync(session.PromptAsync("u2"));

        var context = sessionManager.RebuildContext();
        var userTexts = context
            .Where(m => m.Role == LlmMessageRole.User)
            .Select(m => m.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text)
            .Where(x => x != null)
            .ToList();

        Assert.Equal(["u1", "u2"], userTexts);
    }

    [Fact]
    public async Task PromptAsync_ForwardsSessionOptionsToProviderRequest()
    {
        var provider = new RecordingProvider();
        provider.Enqueue(request =>
        {
            request.OnPayload?.Invoke(JsonSerializer.SerializeToElement(new { kind = "payload-probe" }));
            return [new LlmCompletedEvent("ok", null, [])];
        });

        var sessionManager = await SessionManager.CreateAsync(_tempDir, _tempDir);
        var runtime = new ToolRuntime([]);

        JsonElement? capturedPayload = null;
        var budgets = new ThinkingBudgets(Minimal: 111, Low: 222, Medium: 333, High: 444, XHigh: 555);

        using var session = new AgentSession(
            provider,
            sessionManager,
            runtime,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Low,
            maxTurns: 5,
            maxRetryDelayMs: 1234,
            requestHeaders: new Dictionary<string, string> { ["x-test"] = "v" },
            onPayload: payload => capturedPayload = payload,
            thinkingBudgets: budgets);

        await ConsumeAsync(session.PromptAsync("hello"));

        var request = Assert.Single(provider.Requests);
        Assert.Equal(sessionManager.SessionId, request.SessionId);
        Assert.Equal(1234, request.MaxRetryDelayMs);
        Assert.NotNull(request.Headers);
        Assert.True(request.Headers!.TryGetValue("x-test", out var headerValue));
        Assert.Equal("v", headerValue);
        Assert.NotNull(request.OnPayload);
        Assert.NotNull(request.ThinkingBudgets);
        Assert.Equal(budgets, request.ThinkingBudgets);
        Assert.NotNull(capturedPayload);
        Assert.Equal("payload-probe", capturedPayload!.Value.GetProperty("kind").GetString());
    }

    private static AgentSession CreateSession(
        ILlmProvider provider,
        SessionManager sessionManager,
        ToolRuntime runtime)
        => new(
            provider,
            sessionManager,
            runtime,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 5);

    private static async Task ConsumeAsync(IAsyncEnumerable<AgentEvent> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    private static async Task<List<AgentEvent>> CollectAsync(IAsyncEnumerable<AgentEvent> stream)
    {
        var events = new List<AgentEvent>();
        await foreach (var evt in stream)
            events.Add(evt);
        return events;
    }
}
