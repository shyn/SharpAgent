using System.Runtime.CompilerServices;
using System.Text.Json;
using Sharp.AI;
using Sharp.Core.Extensions;
using Sharp.Core.Sessions;

namespace Sharp.Core.Tests;

public sealed class ExtensionRuntimeTests : IDisposable
{
    private readonly string _tempDir;

    public ExtensionRuntimeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharpagent-extension-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task EmitInputAsync_AppliesTransformsInRegistrationOrder()
    {
        var first = new InlineExtension("first", api =>
        {
            api.OnInput((@event, _, _) =>
                ValueTask.FromResult<ExtensionInputResult?>(ExtensionInputResult.Transform($"{@event.Text}-a")));
        });

        var second = new InlineExtension("second", api =>
        {
            api.OnInput((@event, _, _) =>
                ValueTask.FromResult<ExtensionInputResult?>(ExtensionInputResult.Transform($"{@event.Text}-b")));
        });

        var runtime = await ExtensionRuntime.CreateAsync([first, second], _tempDir);

        var result = await runtime.EmitInputAsync("seed", ExtensionInputSource.Session);

        Assert.Equal(ExtensionInputAction.Transform, result.Action);
        Assert.Equal("seed-a-b", result.Text);
    }

    [Fact]
    public async Task EmitInputAsync_HandledStopsFollowingHandlers()
    {
        var secondHandlerCalled = false;

        var first = new InlineExtension("first", api =>
        {
            api.OnInput((_, _, _) =>
                ValueTask.FromResult<ExtensionInputResult?>(ExtensionInputResult.Handled()));
        });

        var second = new InlineExtension("second", api =>
        {
            api.OnInput((_, _, _) =>
            {
                secondHandlerCalled = true;
                return ValueTask.FromResult<ExtensionInputResult?>(ExtensionInputResult.Continue());
            });
        });

        var runtime = await ExtensionRuntime.CreateAsync([first, second], _tempDir);

        var result = await runtime.EmitInputAsync("seed", ExtensionInputSource.Session);

        Assert.Equal(ExtensionInputAction.Handled, result.Action);
        Assert.False(secondHandlerCalled);
    }

    [Fact]
    public async Task ToolRuntime_RespectsExtensionToolCallBlock()
    {
        var executeCount = 0;

        var blocker = new InlineExtension("blocker", api =>
        {
            api.OnToolCall((_, _, _) =>
                ValueTask.FromResult<ExtensionToolCallDecision?>(new ExtensionToolCallDecision(
                    Block: true,
                    Reason: "blocked by extension")));
        });

        var extensionRuntime = await ExtensionRuntime.CreateAsync([blocker], _tempDir);
        var runtime = new ToolRuntime(
            [new CountingTool(() => executeCount++)],
            new ToolExecutionContext(_tempDir, "session"),
            extensionRuntime);

        var result = await runtime.ExecuteAsync(new ToolCall("call-1", "counting", "{}"));

        Assert.True(result.IsError);
        Assert.Contains("blocked by extension", result.ContentAsText, StringComparison.Ordinal);
        Assert.Equal(0, executeCount);
    }

    [Fact]
    public async Task EmitToolCallAsync_HandlerExceptionTriggersFailSafeBlock()
    {
        var failing = new InlineExtension("failing", api =>
        {
            api.OnToolCall((_, _, _) => throw new InvalidOperationException("boom"));
        });

        var runtime = await ExtensionRuntime.CreateAsync([failing], _tempDir);

        var decision = await runtime.EmitToolCallAsync(new ExtensionToolCallEvent("call-1", "echo", Json("{}")));

        Assert.NotNull(decision);
        Assert.True(decision!.Block);
        Assert.Contains("failing", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("boom", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolRuntime_AppliesToolResultPatchesAsPipeline()
    {
        var first = new InlineExtension("first", api =>
        {
            api.OnToolResult((_, _, _) =>
                ValueTask.FromResult<ExtensionToolResultPatch?>(new ExtensionToolResultPatch(
                    Content: [new TextContentBlock("patched-1")])));
        });

        var second = new InlineExtension("second", api =>
        {
            api.OnToolResult((@event, _, _) =>
            {
                Assert.Equal("patched-1", @event.Result.ContentAsText);
                return ValueTask.FromResult<ExtensionToolResultPatch?>(new ExtensionToolResultPatch(
                    IsError: true,
                    Content: [new TextContentBlock("patched-2")]));
            });
        });

        var extensionRuntime = await ExtensionRuntime.CreateAsync([first, second], _tempDir);
        var runtime = new ToolRuntime(
            [new CountingTool()],
            new ToolExecutionContext(_tempDir, "session"),
            extensionRuntime);

        var result = await runtime.ExecuteAsync(new ToolCall("call-1", "counting", "{}"));

        Assert.True(result.IsError);
        Assert.Equal("patched-2", result.ContentAsText);
    }

    [Fact]
    public async Task CreateAsync_DropsLaterExtensionWhenNamesConflict()
    {
        var primary = new InlineExtension("primary", api =>
        {
            api.RegisterCommand(new ExtensionCommandDefinition(
                "hello",
                static (_, _, _) => ValueTask.CompletedTask));
        });

        var conflicting = new InlineExtension("conflicting", api =>
        {
            api.RegisterCommand(new ExtensionCommandDefinition(
                "hello",
                static (_, _, _) => ValueTask.CompletedTask));
            api.RegisterCommand(new ExtensionCommandDefinition(
                "goodbye",
                static (_, _, _) => ValueTask.CompletedTask));
        });

        var runtime = await ExtensionRuntime.CreateAsync([primary, conflicting], _tempDir);

        Assert.True(runtime.HasCommand("hello"));
        Assert.False(runtime.HasCommand("goodbye"));
        Assert.Contains(runtime.Diagnostics, diagnostic =>
            diagnostic.ExtensionName == "conflicting"
            && diagnostic.Message.Contains("conflicts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmitSessionBeforeTreeAsync_AppliesOverrides()
    {
        var extension = new InlineExtension("tree", api =>
        {
            api.OnSessionBeforeTree((_, _, _) =>
                ValueTask.FromResult<ExtensionSessionBeforeTreeResult?>(new ExtensionSessionBeforeTreeResult(
                    TargetEntryId: "leaf-b",
                    Summarize: true,
                    Label: "patched-label")));
        });

        var runtime = await ExtensionRuntime.CreateAsync([extension], _tempDir);
        var decision = await runtime.EmitSessionBeforeTreeAsync(
            new ExtensionSessionBeforeTreeEvent("session-1", "leaf-a"));

        Assert.NotNull(decision);
        Assert.False(decision!.Cancel);
        Assert.Equal("leaf-b", decision.Event.TargetEntryId);
        Assert.True(decision.Event.Summarize);
        Assert.Equal("patched-label", decision.Event.Label);
    }

    [Fact]
    public async Task AgentSession_RequestSessionSwitchAsync_CanBeCancelledByExtension()
    {
        var extension = new InlineExtension("switch", api =>
        {
            api.OnSessionBeforeSwitch((_, _, _) =>
                ValueTask.FromResult<ExtensionSessionBeforeSwitchResult?>(new ExtensionSessionBeforeSwitchResult(
                    Cancel: true)));
        });

        var extensionRuntime = await ExtensionRuntime.CreateAsync([extension], _tempDir);
        var provider = new GuardProvider();
        var sessionManager = await SessionManager.CreateAsync(
            Path.Combine(_tempDir, "sessions-switch"),
            _tempDir);
        var tools = new ToolRuntime([], new ToolExecutionContext(_tempDir, sessionManager.SessionId), extensionRuntime);

        using var session = new AgentSession(
            provider,
            sessionManager,
            tools,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 3,
            extensionRuntime: extensionRuntime);

        var allowed = await session.RequestSessionSwitchAsync(
            ExtensionSessionSwitchReason.Resume,
            "target-session");

        Assert.False(allowed);
    }

    [Fact]
    public async Task AgentSession_NotifySessionSwitchedAsync_EmitsSessionSwitchEvent()
    {
        ExtensionSessionSwitchEvent? observedEvent = null;

        var extension = new InlineExtension("switch-observer", api =>
        {
            api.OnSessionSwitch((@event, _, _) =>
            {
                observedEvent = @event;
                return ValueTask.CompletedTask;
            });
        });

        var extensionRuntime = await ExtensionRuntime.CreateAsync([extension], _tempDir);
        var provider = new GuardProvider();
        var sessionManager = await SessionManager.CreateAsync(
            Path.Combine(_tempDir, "sessions-switch-observer"),
            _tempDir);
        var tools = new ToolRuntime([], new ToolExecutionContext(_tempDir, sessionManager.SessionId), extensionRuntime);

        using var session = new AgentSession(
            provider,
            sessionManager,
            tools,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 3,
            extensionRuntime: extensionRuntime);

        await session.NotifySessionSwitchedAsync(
            ExtensionSessionSwitchReason.Resume,
            previousSessionId: "session-old",
            targetSessionId: "session-new");

        Assert.NotNull(observedEvent);
        Assert.Equal(ExtensionSessionSwitchReason.Resume, observedEvent!.Reason);
        Assert.Equal("session-old", observedEvent.PreviousSessionId);
        Assert.Equal("session-new", observedEvent.TargetSessionId);
    }

    [Fact]
    public async Task AgentSession_ForkBranchAsync_CanBeCancelledByExtension()
    {
        var extension = new InlineExtension("fork", api =>
        {
            api.OnSessionBeforeFork((_, _, _) =>
                ValueTask.FromResult<ExtensionSessionBeforeForkResult?>(new ExtensionSessionBeforeForkResult(
                    Cancel: true)));
        });

        var extensionRuntime = await ExtensionRuntime.CreateAsync([extension], _tempDir);
        var provider = new GuardProvider();
        var sessionManager = await SessionManager.CreateAsync(
            Path.Combine(_tempDir, "sessions-fork"),
            _tempDir);
        var root = await sessionManager.AppendMessageAsync(LlmMessage.UserText("u1"));
        _ = await sessionManager.AppendMessageAsync(LlmMessage.AssistantText("a1"));
        var currentLeafBeforeFork = sessionManager.CurrentLeafId;

        var tools = new ToolRuntime([], new ToolExecutionContext(_tempDir, sessionManager.SessionId), extensionRuntime);
        using var session = new AgentSession(
            provider,
            sessionManager,
            tools,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 3,
            extensionRuntime: extensionRuntime);

        var forked = await session.ForkBranchAsync(root.Id);

        Assert.False(forked);
        Assert.Equal(currentLeafBeforeFork, sessionManager.CurrentLeafId);
    }

    [Fact]
    public async Task AgentSession_ForkBranchAsync_EmitsSessionForkEvent()
    {
        ExtensionSessionForkEvent? observedEvent = null;

        var extension = new InlineExtension("fork-observer", api =>
        {
            api.OnSessionFork((@event, _, _) =>
            {
                observedEvent = @event;
                return ValueTask.CompletedTask;
            });
        });

        var extensionRuntime = await ExtensionRuntime.CreateAsync([extension], _tempDir);
        var provider = new GuardProvider();
        var sessionManager = await SessionManager.CreateAsync(
            Path.Combine(_tempDir, "sessions-fork-observer"),
            _tempDir);
        var root = await sessionManager.AppendMessageAsync(LlmMessage.UserText("u1"));
        _ = await sessionManager.AppendMessageAsync(LlmMessage.AssistantText("a1"));
        var previousLeaf = sessionManager.CurrentLeafId;

        var tools = new ToolRuntime([], new ToolExecutionContext(_tempDir, sessionManager.SessionId), extensionRuntime);
        using var session = new AgentSession(
            provider,
            sessionManager,
            tools,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 3,
            extensionRuntime: extensionRuntime);

        var forked = await session.ForkBranchAsync(root.Id);

        Assert.True(forked);
        Assert.NotNull(observedEvent);
        Assert.Equal(root.Id, observedEvent!.EntryId);
        Assert.Equal(previousLeaf, observedEvent.PreviousLeafId);
        Assert.Equal(root.Id, observedEvent.NewLeafId);
    }

    [Fact]
    public async Task AgentSession_ForkBranchAsync_Cancelled_DoesNotEmitSessionForkEvent()
    {
        var observed = false;

        var extension = new InlineExtension("fork-cancel-observer", api =>
        {
            api.OnSessionBeforeFork((_, _, _) =>
                ValueTask.FromResult<ExtensionSessionBeforeForkResult?>(new ExtensionSessionBeforeForkResult(
                    Cancel: true)));
            api.OnSessionFork((_, _, _) =>
            {
                observed = true;
                return ValueTask.CompletedTask;
            });
        });

        var extensionRuntime = await ExtensionRuntime.CreateAsync([extension], _tempDir);
        var provider = new GuardProvider();
        var sessionManager = await SessionManager.CreateAsync(
            Path.Combine(_tempDir, "sessions-fork-cancel-observer"),
            _tempDir);
        var root = await sessionManager.AppendMessageAsync(LlmMessage.UserText("u1"));
        _ = await sessionManager.AppendMessageAsync(LlmMessage.AssistantText("a1"));
        var currentLeafBeforeFork = sessionManager.CurrentLeafId;

        var tools = new ToolRuntime([], new ToolExecutionContext(_tempDir, sessionManager.SessionId), extensionRuntime);
        using var session = new AgentSession(
            provider,
            sessionManager,
            tools,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 3,
            extensionRuntime: extensionRuntime);

        var forked = await session.ForkBranchAsync(root.Id);

        Assert.False(forked);
        Assert.False(observed);
        Assert.Equal(currentLeafBeforeFork, sessionManager.CurrentLeafId);
    }

    [Fact]
    public async Task AgentSession_NavigateTreeAsync_CanBeCancelledByExtension()
    {
        var extension = new InlineExtension("tree-cancel", api =>
        {
            api.OnSessionBeforeTree((_, _, _) =>
                ValueTask.FromResult<ExtensionSessionBeforeTreeResult?>(new ExtensionSessionBeforeTreeResult(
                    Cancel: true)));
        });

        var extensionRuntime = await ExtensionRuntime.CreateAsync([extension], _tempDir);
        var provider = new GuardProvider();
        var sessionManager = await SessionManager.CreateAsync(
            Path.Combine(_tempDir, "sessions-tree"),
            _tempDir);
        var root = await sessionManager.AppendMessageAsync(LlmMessage.UserText("u1"));
        _ = await sessionManager.AppendMessageAsync(LlmMessage.AssistantText("a1"));
        var currentLeafBeforeNavigate = sessionManager.CurrentLeafId;

        var tools = new ToolRuntime([], new ToolExecutionContext(_tempDir, sessionManager.SessionId), extensionRuntime);
        using var session = new AgentSession(
            provider,
            sessionManager,
            tools,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 3,
            extensionRuntime: extensionRuntime);

        var navigated = await session.NavigateTreeAsync(root.Id);

        Assert.False(navigated);
        Assert.Equal(currentLeafBeforeNavigate, sessionManager.CurrentLeafId);
    }

    [Fact]
    public async Task AgentSession_NavigateTreeAsync_EmitsSessionTreeEvent()
    {
        ExtensionSessionTreeEvent? observedEvent = null;

        var extension = new InlineExtension("tree-observer", api =>
        {
            api.OnSessionTree((@event, _, _) =>
            {
                observedEvent = @event;
                return ValueTask.CompletedTask;
            });
        });

        var extensionRuntime = await ExtensionRuntime.CreateAsync([extension], _tempDir);
        var provider = new GuardProvider();
        var sessionManager = await SessionManager.CreateAsync(
            Path.Combine(_tempDir, "sessions-tree-observer"),
            _tempDir);
        var root = await sessionManager.AppendMessageAsync(LlmMessage.UserText("u1"));
        _ = await sessionManager.AppendMessageAsync(LlmMessage.AssistantText("a1"));
        var previousLeaf = sessionManager.CurrentLeafId;

        var tools = new ToolRuntime([], new ToolExecutionContext(_tempDir, sessionManager.SessionId), extensionRuntime);
        using var session = new AgentSession(
            provider,
            sessionManager,
            tools,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 3,
            extensionRuntime: extensionRuntime);

        var navigated = await session.NavigateTreeAsync(root.Id, summarize: true, label: "nav-label");

        Assert.True(navigated);
        Assert.NotNull(observedEvent);
        Assert.Equal(root.Id, observedEvent!.TargetEntryId);
        Assert.Equal(previousLeaf, observedEvent.PreviousLeafId);
        Assert.Equal(root.Id, observedEvent.NewLeafId);
        Assert.True(observedEvent.Summarize);
        Assert.Equal("nav-label", observedEvent.Label);
    }

    [Fact]
    public async Task AgentSession_NavigateTreeAsync_Cancelled_DoesNotEmitSessionTreeEvent()
    {
        var observed = false;

        var extension = new InlineExtension("tree-cancel-observer", api =>
        {
            api.OnSessionBeforeTree((_, _, _) =>
                ValueTask.FromResult<ExtensionSessionBeforeTreeResult?>(new ExtensionSessionBeforeTreeResult(
                    Cancel: true)));
            api.OnSessionTree((_, _, _) =>
            {
                observed = true;
                return ValueTask.CompletedTask;
            });
        });

        var extensionRuntime = await ExtensionRuntime.CreateAsync([extension], _tempDir);
        var provider = new GuardProvider();
        var sessionManager = await SessionManager.CreateAsync(
            Path.Combine(_tempDir, "sessions-tree-cancel-observer"),
            _tempDir);
        var root = await sessionManager.AppendMessageAsync(LlmMessage.UserText("u1"));
        _ = await sessionManager.AppendMessageAsync(LlmMessage.AssistantText("a1"));
        var currentLeafBeforeNavigate = sessionManager.CurrentLeafId;

        var tools = new ToolRuntime([], new ToolExecutionContext(_tempDir, sessionManager.SessionId), extensionRuntime);
        using var session = new AgentSession(
            provider,
            sessionManager,
            tools,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 3,
            extensionRuntime: extensionRuntime);

        var navigated = await session.NavigateTreeAsync(root.Id);

        Assert.False(navigated);
        Assert.False(observed);
        Assert.Equal(currentLeafBeforeNavigate, sessionManager.CurrentLeafId);
    }

    [Fact]
    public async Task AgentSession_NavigateTreeAsync_EmitsSessionTreeEventWithBeforeOverrides()
    {
        ExtensionSessionTreeEvent? observedEvent = null;
        var overriddenTargetId = string.Empty;

        var extension = new InlineExtension("tree-overrides", api =>
        {
            api.OnSessionBeforeTree((_, _, _) =>
                ValueTask.FromResult<ExtensionSessionBeforeTreeResult?>(new ExtensionSessionBeforeTreeResult(
                    TargetEntryId: overriddenTargetId,
                    Summarize: true,
                    CustomInstructions: "custom",
                    ReplaceInstructions: true,
                    Label: "patched")));
            api.OnSessionTree((@event, _, _) =>
            {
                observedEvent = @event;
                return ValueTask.CompletedTask;
            });
        });

        var extensionRuntime = await ExtensionRuntime.CreateAsync([extension], _tempDir);
        var provider = new GuardProvider();
        var sessionManager = await SessionManager.CreateAsync(
            Path.Combine(_tempDir, "sessions-tree-overrides"),
            _tempDir);
        var root = await sessionManager.AppendMessageAsync(LlmMessage.UserText("u1"));
        _ = await sessionManager.AppendMessageAsync(LlmMessage.AssistantText("a1"));
        overriddenTargetId = root.Id;
        var previousLeaf = sessionManager.CurrentLeafId;

        var tools = new ToolRuntime([], new ToolExecutionContext(_tempDir, sessionManager.SessionId), extensionRuntime);
        using var session = new AgentSession(
            provider,
            sessionManager,
            tools,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 3,
            extensionRuntime: extensionRuntime);

        var navigated = await session.NavigateTreeAsync(previousLeaf!);

        Assert.True(navigated);
        Assert.NotNull(observedEvent);
        Assert.Equal(root.Id, observedEvent!.TargetEntryId);
        Assert.Equal(previousLeaf, observedEvent.PreviousLeafId);
        Assert.Equal(root.Id, observedEvent.NewLeafId);
        Assert.True(observedEvent.Summarize);
        Assert.Equal("custom", observedEvent.CustomInstructions);
        Assert.True(observedEvent.ReplaceInstructions);
        Assert.Equal("patched", observedEvent.Label);
    }

    [Fact]
    public async Task AgentSession_AppendCompactionAsync_AppliesExtensionOverrides()
    {
        ExtensionSessionCompactEvent? observedEvent = null;

        var extension = new InlineExtension("compact", api =>
        {
            api.OnSessionBeforeCompact((@event, _, _) =>
                ValueTask.FromResult<ExtensionSessionBeforeCompactResult?>(new ExtensionSessionBeforeCompactResult(
                    Summary: "patched-summary",
                    TokensBefore: @event.TokensBefore + 100,
                    FromHook: true)));
            api.OnSessionCompact((@event, _, _) =>
            {
                observedEvent = @event;
                return ValueTask.CompletedTask;
            });
        });

        var extensionRuntime = await ExtensionRuntime.CreateAsync([extension], _tempDir);
        var provider = new GuardProvider();
        var sessionManager = await SessionManager.CreateAsync(
            Path.Combine(_tempDir, "sessions-compact"),
            _tempDir);
        var keep = await sessionManager.AppendMessageAsync(LlmMessage.UserText("u1"));
        _ = await sessionManager.AppendMessageAsync(LlmMessage.AssistantText("a1"));

        var tools = new ToolRuntime([], new ToolExecutionContext(_tempDir, sessionManager.SessionId), extensionRuntime);
        using var session = new AgentSession(
            provider,
            sessionManager,
            tools,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 3,
            extensionRuntime: extensionRuntime);

        var entry = await session.AppendCompactionAsync(
            summary: "original-summary",
            firstKeptEntryId: keep.Id,
            tokensBefore: 42);

        Assert.NotNull(entry);
        var payload = entry!.GetPayload<CompactionEntryPayload>();
        Assert.NotNull(payload);
        Assert.Equal("patched-summary", payload!.Summary);
        Assert.Equal(142, payload.TokensBefore);
        Assert.True(payload.FromHook);
        Assert.NotNull(observedEvent);
        Assert.Equal(entry.Id, observedEvent!.EntryId);
        Assert.Equal("patched-summary", observedEvent.Summary);
        Assert.Equal(142, observedEvent.TokensBefore);
        Assert.True(observedEvent.FromHook);
    }

    [Fact]
    public async Task AgentSession_AppendCompactionAsync_Cancelled_DoesNotEmitSessionCompactEvent()
    {
        var observed = false;

        var extension = new InlineExtension("compact-cancel", api =>
        {
            api.OnSessionBeforeCompact((_, _, _) =>
                ValueTask.FromResult<ExtensionSessionBeforeCompactResult?>(new ExtensionSessionBeforeCompactResult(
                    Cancel: true)));
            api.OnSessionCompact((_, _, _) =>
            {
                observed = true;
                return ValueTask.CompletedTask;
            });
        });

        var extensionRuntime = await ExtensionRuntime.CreateAsync([extension], _tempDir);
        var provider = new GuardProvider();
        var sessionManager = await SessionManager.CreateAsync(
            Path.Combine(_tempDir, "sessions-compact-cancel"),
            _tempDir);
        var keep = await sessionManager.AppendMessageAsync(LlmMessage.UserText("u1"));
        _ = await sessionManager.AppendMessageAsync(LlmMessage.AssistantText("a1"));
        var entriesBefore = sessionManager.Entries.Count;
        var leafBefore = sessionManager.CurrentLeafId;

        var tools = new ToolRuntime([], new ToolExecutionContext(_tempDir, sessionManager.SessionId), extensionRuntime);
        using var session = new AgentSession(
            provider,
            sessionManager,
            tools,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 3,
            extensionRuntime: extensionRuntime);

        var entry = await session.AppendCompactionAsync(
            summary: "original-summary",
            firstKeptEntryId: keep.Id,
            tokensBefore: 42);

        Assert.Null(entry);
        Assert.False(observed);
        Assert.Equal(entriesBefore, sessionManager.Entries.Count);
        Assert.Equal(leafBefore, sessionManager.CurrentLeafId);
    }

    [Fact]
    public async Task AgentSession_PromptAsync_ExecutesSlashCommandWithoutLlmCall()
    {
        var commandArgs = string.Empty;
        var commandExecuted = false;

        var extension = new InlineExtension("commands", api =>
        {
            api.RegisterCommand(new ExtensionCommandDefinition(
                "hello",
                (args, _, _) =>
                {
                    commandArgs = args;
                    commandExecuted = true;
                    return ValueTask.CompletedTask;
                }));
        });

        var extensionRuntime = await ExtensionRuntime.CreateAsync([extension], _tempDir);
        var provider = new GuardProvider();
        var sessionManager = await SessionManager.CreateAsync(
            Path.Combine(_tempDir, "sessions-command"),
            _tempDir);
        var tools = new ToolRuntime([], new ToolExecutionContext(_tempDir, sessionManager.SessionId), extensionRuntime);

        using var session = new AgentSession(
            provider,
            sessionManager,
            tools,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 3,
            extensionRuntime: extensionRuntime);

        await foreach (var _ in session.PromptAsync("/hello world"))
        {
        }

        Assert.True(commandExecuted);
        Assert.Equal("world", commandArgs);
        Assert.False(provider.Called);
        Assert.Empty(sessionManager.RebuildContext());
    }

    [Fact]
    public async Task AgentSession_PromptAsync_AppliesInputAndBeforeAgentStartHooks()
    {
        var extension = new InlineExtension("transform", api =>
        {
            api.OnInput((@event, _, _) =>
                ValueTask.FromResult<ExtensionInputResult?>(ExtensionInputResult.Transform(@event.Text.ToUpperInvariant())));
            api.OnBeforeAgentStart((_, _, _) =>
                ValueTask.FromResult<ExtensionBeforeAgentStartResult?>(new ExtensionBeforeAgentStartResult(
                    Messages: [LlmMessage.AssistantText("from-extension")],
                    SystemPrompt: "overridden-system")));
        });

        var extensionRuntime = await ExtensionRuntime.CreateAsync([extension], _tempDir);
        var provider = new CaptureProvider();
        var sessionManager = await SessionManager.CreateAsync(
            Path.Combine(_tempDir, "sessions-hooks"),
            _tempDir);
        var tools = new ToolRuntime([], new ToolExecutionContext(_tempDir, sessionManager.SessionId), extensionRuntime);

        using var session = new AgentSession(
            provider,
            sessionManager,
            tools,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "original-system",
            ThinkingLevel.Off,
            maxTurns: 3,
            extensionRuntime: extensionRuntime);

        await foreach (var _ in session.PromptAsync("hello plugin"))
        {
        }

        var request = Assert.Single(provider.Requests);
        Assert.Equal("overridden-system", request.SystemPrompt);
        Assert.Contains(request.Messages, message =>
            message.Role == LlmMessageRole.User
            && message.Content.OfType<TextContentBlock>().Any(content => content.Text == "HELLO PLUGIN"));
        Assert.Contains(request.Messages, message =>
            message.Role == LlmMessageRole.Assistant
            && message.Content.OfType<TextContentBlock>().Any(content => content.Text == "from-extension"));

        var context = sessionManager.RebuildContext();
        Assert.Contains(context, message =>
            message.Role == LlmMessageRole.User
            && message.Content.OfType<TextContentBlock>().Any(content => content.Text == "HELLO PLUGIN"));
        Assert.Contains(context, message =>
            message.Role == LlmMessageRole.Assistant
            && message.Content.OfType<TextContentBlock>().Any(content => content.Text == "from-extension"));
    }

    [Fact]
    public async Task AgentSession_CreateAsync_LoadsSkillsFromExtensionResourceDiscovery()
    {
        var cwd = Path.Combine(_tempDir, "workspace");
        var skillsRoot = Path.Combine(_tempDir, "extension-skills");
        var skillDir = Path.Combine(skillsRoot, "calendar-helper");
        Directory.CreateDirectory(Path.Combine(cwd, ".sharp"));
        Directory.CreateDirectory(skillDir);

        File.WriteAllText(
            Path.Combine(skillDir, "SKILL.md"),
            """
            ---
            name: calendar-helper
            description: Calendar utility skill
            ---
            # Calendar Helper
            Body
            """);

        var extension = new InlineExtension("resources", api =>
        {
            api.OnResourcesDiscover((_, _, _) =>
                ValueTask.FromResult<ExtensionResourcesDiscoverResult?>(new ExtensionResourcesDiscoverResult(
                    SkillPaths: [skillsRoot],
                    PromptPaths: ["/tmp/prompt"],
                    ThemePaths: ["/tmp/theme"])));
        });

        var options = new AgentRuntimeOptions
        {
            Model = new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            ApiKey = "test-key",
            BaseUrl = "https://example.com/v1/",
            WorkingDirectory = cwd,
            SessionDirectory = Path.Combine(_tempDir, "sessions-create"),
            AgentDirectory = Path.Combine(_tempDir, "agent-create"),
            SystemPrompt = "base-system",
            EnableSkills = true,
            IncludeDefaultSkills = false,
            EnableExtensions = true,
            Extensions = [extension]
        };

        using var session = await AgentSession.CreateAsync(options);

        Assert.Single(session.ResourceSnapshot.Skills);
        Assert.Contains("calendar-helper", session.ResourceSnapshot.Skills[0].Name, StringComparison.Ordinal);
        Assert.Contains("<available_skills>", session.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains(session.ResourceSnapshot.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("prompt paths are not supported", StringComparison.Ordinal));
        Assert.Contains(session.ResourceSnapshot.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("theme paths are not supported", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AgentSession_ReloadExtensionsAsync_RebuildsLifecycleCommandsAndSkills()
    {
        var cwd = Path.Combine(_tempDir, "workspace-reload");
        var agentDir = Path.Combine(_tempDir, "agent-reload");
        Directory.CreateDirectory(Path.Combine(cwd, ".sharp"));
        Directory.CreateDirectory(agentDir);

        var skillRootV1 = Path.Combine(_tempDir, "reload-skill-v1");
        var skillRootV2 = Path.Combine(_tempDir, "reload-skill-v2");
        CreateSkill(skillRootV1, "reload-skill-v1");
        CreateSkill(skillRootV2, "reload-skill-v2");

        var extension = new ReloadableInlineExtension(
            commandName: "v1",
            skillPath: skillRootV1);

        var options = new AgentRuntimeOptions
        {
            Model = new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            ApiKey = "test-key",
            BaseUrl = "https://example.com/v1/",
            WorkingDirectory = cwd,
            SessionDirectory = Path.Combine(_tempDir, "sessions-reload"),
            AgentDirectory = agentDir,
            SystemPrompt = "base-system",
            EnableSkills = true,
            IncludeDefaultSkills = false,
            EnableExtensions = true,
            DiscoverExtensions = false,
            Extensions = [extension]
        };

        using var session = await AgentSession.CreateAsync(options);

        Assert.Equal(1, extension.SessionStartCount);
        Assert.Equal(0, extension.SessionShutdownCount);
        Assert.Contains("<name>reload-skill-v1</name>", session.SystemPrompt, StringComparison.Ordinal);

        await foreach (var _ in session.PromptAsync("/v1 alpha"))
        {
        }

        Assert.Equal("v1:alpha", extension.LastCommandInvocation);

        extension.CommandName = "v2";
        extension.SkillPath = skillRootV2;

        await session.ReloadExtensionsAsync();

        Assert.Equal(2, extension.SessionStartCount);
        Assert.Equal(1, extension.SessionShutdownCount);
        Assert.Equal(session.SystemPrompt, session.ResourceSnapshot.FinalSystemPrompt);
        Assert.DoesNotContain("<name>reload-skill-v1</name>", session.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("<name>reload-skill-v2</name>", session.SystemPrompt, StringComparison.Ordinal);
        Assert.Single(session.ResourceSnapshot.Skills);
        Assert.Equal("reload-skill-v2", session.ResourceSnapshot.Skills[0].Name);

        await foreach (var _ in session.PromptAsync("/v2 beta"))
        {
        }

        Assert.Equal("v2:beta", extension.LastCommandInvocation);
    }

    [Fact]
    public async Task AgentSession_ReloadExtensionsAsync_WithManualConstructor_Throws()
    {
        var provider = new GuardProvider();
        var sessionManager = await SessionManager.CreateAsync(
            Path.Combine(_tempDir, "sessions-reload-manual"),
            _tempDir);
        var toolRuntime = new ToolRuntime([], new ToolExecutionContext(_tempDir, sessionManager.SessionId));

        using var session = new AgentSession(
            provider,
            sessionManager,
            toolRuntime,
            new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            "system",
            ThinkingLevel.Off,
            maxTurns: 3);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReloadExtensionsAsync());
        Assert.Contains("CreateAsync", error.Message, StringComparison.Ordinal);
    }

    private static void CreateSkill(string skillsRoot, string skillName)
    {
        var skillDir = Path.Combine(skillsRoot, skillName);
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(
            Path.Combine(skillDir, "SKILL.md"),
            $$"""
            ---
            name: {{skillName}}
            description: Reload test skill {{skillName}}
            ---
            # {{skillName}}
            Body
            """);
    }

    private static JsonElement Json(string json)
        => JsonSerializer.Deserialize<JsonElement>(json, JsonDefaults.Options);

    private sealed class InlineExtension : IAgentExtension
    {
        private readonly Action<IAgentExtensionApi> _configure;

        public InlineExtension(string name, Action<IAgentExtensionApi> configure)
        {
            Name = name;
            _configure = configure;
        }

        public string Name { get; }

        public ValueTask InitializeAsync(IAgentExtensionApi api, CancellationToken ct = default)
        {
            _configure(api);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingTool : IAgentTool
    {
        private readonly Action? _onExecute;

        public CountingTool(Action? onExecute = null)
        {
            _onExecute = onExecute;
        }

        public string Name => "counting";

        public string Description => "Counts executions";

        public JsonElement ParametersSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { }
        }, JsonDefaults.Options);

        public Task<ToolInvocationResult> ExecuteAsync(
            JsonElement arguments,
            ToolExecutionContext context,
            IProgress<ToolInvocationResult>? progress = null,
            CancellationToken ct = default)
        {
            _onExecute?.Invoke();
            return Task.FromResult(ToolInvocationResult.Text("tool-result"));
        }
    }

    private sealed class GuardProvider : ILlmProvider
    {
        public string ProviderId => "guard";

        public bool Called { get; private set; }

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            Called = true;
            throw new InvalidOperationException("GuardProvider should never be called");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public void Dispose()
        {
        }
    }

    private sealed class CaptureProvider : ILlmProvider
    {
        private readonly List<LlmRequest> _requests = [];

        public string ProviderId => "capture";

        public IReadOnlyList<LlmRequest> Requests => _requests;

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            _requests.Add(request);
            yield return new LlmCompletedEvent("done", null, []);
            await Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class ReloadableInlineExtension : IAgentExtension
    {
        public ReloadableInlineExtension(string commandName, string skillPath)
        {
            CommandName = commandName;
            SkillPath = skillPath;
        }

        public string Name => "reloadable-inline";

        public string CommandName { get; set; }

        public string SkillPath { get; set; }

        public int SessionStartCount { get; private set; }

        public int SessionShutdownCount { get; private set; }

        public string? LastCommandInvocation { get; private set; }

        public ValueTask InitializeAsync(IAgentExtensionApi api, CancellationToken ct = default)
        {
            var commandName = CommandName;
            api.RegisterCommand(new ExtensionCommandDefinition(
                commandName,
                (args, _, _) =>
                {
                    LastCommandInvocation = $"{commandName}:{args}";
                    return ValueTask.CompletedTask;
                }));

            api.OnResourcesDiscover((_, _, _) =>
                ValueTask.FromResult<ExtensionResourcesDiscoverResult?>(new ExtensionResourcesDiscoverResult(
                    SkillPaths: [SkillPath])));

            api.OnSessionStart((_, _, _) =>
            {
                SessionStartCount++;
                return ValueTask.CompletedTask;
            });

            api.OnSessionShutdown((_, _, _) =>
            {
                SessionShutdownCount++;
                return ValueTask.CompletedTask;
            });

            return ValueTask.CompletedTask;
        }
    }
}
