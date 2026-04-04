using System.Runtime.CompilerServices;
using Sharp.AI;
using Sharp.Core.Extensions;
using Sharp.Core.Resources;
using Sharp.Core.Sessions;
using Sharp.Core.Tools;

namespace Sharp.Core;

public sealed class AgentSession : IDisposable, IExtensionRuntimeHost
{
    private readonly ILlmProvider _provider;
    private AgentLoop _loop;
    private readonly object _stateLock = new();
    private readonly Queue<LlmMessage> _steeringQueue = [];
    private readonly Queue<LlmMessage> _followUpQueue = [];
    private readonly SemaphoreSlim _extensionReloadLock = new(1, 1);

    private CancellationTokenSource? _runningCts;
    private TaskCompletionSource<bool>? _runningTcs;

    private readonly int? _maxRetryDelayMs;
    private readonly IReadOnlyDictionary<string, string>? _requestHeaders;
    private readonly Action<System.Text.Json.JsonElement>? _onPayload;
    private readonly Action<string>? _onDebugLog;
    private readonly ThinkingBudgets? _thinkingBudgets;
    private readonly string _workingDirectory;
    private readonly AgentRuntimeOptions? _reloadOptions;
    private readonly IReadOnlyList<IAgentTool>? _baseToolsForReload;

    private ExtensionRuntime? _extensionRuntime;
    private SessionResourceSnapshot _resourceSnapshot;

    public SessionManager SessionManager { get; }
    public ToolRuntime ToolRuntime { get; private set; }
    public ModelDescriptor Model { get; }
    public SessionResourceSnapshot ResourceSnapshot => _resourceSnapshot;
    public ExtensionRuntime? ExtensionRuntime => _extensionRuntime;

    public string SystemPrompt { get; set; }
    public ThinkingLevel ThinkingLevel { get; set; }
    public int MaxTurns { get; set; }

    public QueueDeliveryMode SteeringMode { get; set; } = QueueDeliveryMode.OneAtATime;
    public QueueDeliveryMode FollowUpMode { get; set; } = QueueDeliveryMode.OneAtATime;

    public Func<IReadOnlyList<LlmMessage>, CancellationToken, Task<IReadOnlyList<LlmMessage>>>? TransformContext { get; set; }
    public Func<IReadOnlyList<LlmMessage>, CancellationToken, Task<IReadOnlyList<LlmMessage>>>? ConvertToLlm { get; set; }

    public bool IsStreaming
    {
        get
        {
            lock (_stateLock)
            {
                return _runningTcs is { Task.IsCompleted: false };
            }
        }
    }

    public AgentSession(
        ILlmProvider provider,
        SessionManager sessionManager,
        ToolRuntime toolRuntime,
        ModelDescriptor model,
        string systemPrompt,
        ThinkingLevel thinkingLevel,
        int maxTurns,
        int? maxRetryDelayMs = 60000,
        IReadOnlyDictionary<string, string>? requestHeaders = null,
        Action<System.Text.Json.JsonElement>? onPayload = null,
        Action<string>? onDebugLog = null,
        ThinkingBudgets? thinkingBudgets = null,
        SessionResourceSnapshot? resourceSnapshot = null,
        ExtensionRuntime? extensionRuntime = null,
        AgentRuntimeOptions? reloadOptions = null,
        IReadOnlyList<IAgentTool>? baseToolsForReload = null)
    {
        _provider = provider;
        _loop = new AgentLoop(provider, toolRuntime);

        SessionManager = sessionManager;
        ToolRuntime = toolRuntime;
        Model = model;
        SystemPrompt = systemPrompt;
        ThinkingLevel = thinkingLevel;
        MaxTurns = maxTurns;
        _resourceSnapshot = resourceSnapshot ?? SessionResourceSnapshot.Empty(systemPrompt);

        _maxRetryDelayMs = maxRetryDelayMs;
        _requestHeaders = requestHeaders;
        _onPayload = onPayload;
        _onDebugLog = onDebugLog;
        _thinkingBudgets = thinkingBudgets;
        _extensionRuntime = extensionRuntime;
        _workingDirectory = sessionManager.Header.WorkingDirectory;
        _reloadOptions = reloadOptions;
        _baseToolsForReload = baseToolsForReload;

        _extensionRuntime?.BindHost(this);
    }

    public static async Task<AgentSession> CreateAsync(
        AgentRuntimeOptions options,
        IEnumerable<IAgentTool>? tools = null,
        string? sessionId = null,
        CancellationToken ct = default)
    {
        ExtensionRuntime? extensionRuntime = null;
        var extensionDiscoveryDiagnostics = new List<ExtensionDiagnostic>();

        if (options.EnableExtensions)
        {
            var extensions = new List<IAgentExtension>();
            if (options.Extensions is { Count: > 0 })
                extensions.AddRange(options.Extensions);

            if (options.DiscoverExtensions || options.ExtensionPaths is { Count: > 0 })
            {
                var discovered = ExtensionLoader.DiscoverAndLoad(
                    options.WorkingDirectory,
                    options.AgentDirectory,
                    options.ExtensionPaths,
                    includeDefaultDirectories: options.DiscoverExtensions);
                extensionDiscoveryDiagnostics.AddRange(discovered.Diagnostics);
                extensions.AddRange(discovered.Extensions);
            }

            if (extensions.Count > 0)
            {
                extensionRuntime = await ExtensionRuntime.CreateAsync(
                    extensions,
                    options.WorkingDirectory,
                    options.ExtensionFlagValues,
                    ct);

                foreach (var registration in extensionRuntime.ProviderFactories)
                {
                    LlmProviderFactory.Register(
                        registration.ApiKind,
                        registration.Factory,
                        overwrite: registration.Overwrite);
                }
            }
        }

        var provider = LlmProviderFactory.Create(
            options.Model,
            options.ApiKey,
            options.BaseUrl,
            options.CredentialProvider);
        var sessionManager = await SessionManager.CreateAsync(
            options.SessionDirectory,
            options.WorkingDirectory,
            sessionId,
            ct);

        var baseTools = tools?.ToList() ??
                        [
                            new ReadTool(options.WorkingDirectory),
                            new WriteTool(options.WorkingDirectory, options.AllowWriteOutsideWorkspace),
                            new EditTool(options.WorkingDirectory, options.AllowWriteOutsideWorkspace),
                            new BashTool(options.WorkingDirectory),
                            new GrepTool(options.WorkingDirectory),
                            new FindTool(options.WorkingDirectory),
                            new LsTool(options.WorkingDirectory)
                        ];

        var extensionToolAdapters = extensionRuntime?.CreateRegisteredTools() ?? [];
        var effectiveTools = new List<IAgentTool>(baseTools.Count + extensionToolAdapters.Count);
        effectiveTools.AddRange(baseTools);
        effectiveTools.AddRange(extensionToolAdapters);

        var extensionResources = extensionRuntime == null
            ? new ExtensionResourcesDiscoverResult()
            : await extensionRuntime.EmitResourcesDiscoverAsync(ExtensionResourcesDiscoverReason.Startup, ct);
        var mergedSkillPaths = MergePaths(options.SkillPaths, extensionResources.SkillPaths);

        var resourceLoader = new SessionResourceLoader();
        var resourceLoadResult = resourceLoader.Load(new SessionResourceOptions(
            WorkingDirectory: options.WorkingDirectory,
            AgentDirectory: options.AgentDirectory,
            BaseSystemPrompt: options.SystemPrompt,
            AppendSystemPrompt: options.AppendSystemPrompt,
            DiscoverSystemPromptFile: options.DiscoverSystemPromptFile,
            IncludeProjectContextFiles: options.IncludeProjectContextFiles,
            EnableSkills: options.EnableSkills,
            IncludeDefaultSkills: options.IncludeDefaultSkills,
            SkillPaths: mergedSkillPaths));

        var resourceDiagnostics = BuildResourceDiagnostics(
            resourceLoadResult.Diagnostics,
            extensionDiscoveryDiagnostics,
            extensionRuntime,
            extensionResources);

        var includeSkillsInPrompt = effectiveTools.Any(x => string.Equals(x.Name, "read", StringComparison.Ordinal));
        var finalSystemPrompt = SystemPromptBuilder.Build(
            resourceLoadResult.BaseSystemPrompt,
            resourceLoadResult.AppendSystemPromptSections,
            resourceLoadResult.ContextFiles,
            resourceLoadResult.Skills,
            includeSkillsInPrompt);

        var resourceSnapshot = new SessionResourceSnapshot(
            BaseSystemPrompt: resourceLoadResult.BaseSystemPrompt,
            FinalSystemPrompt: finalSystemPrompt,
            AppendSystemPromptSections: resourceLoadResult.AppendSystemPromptSections,
            ContextFiles: resourceLoadResult.ContextFiles,
            Skills: resourceLoadResult.Skills,
            SkillDiagnostics: resourceLoadResult.SkillDiagnostics,
            Diagnostics: resourceDiagnostics);

        var toolRuntime = new ToolRuntime(
            effectiveTools,
            new ToolExecutionContext(options.WorkingDirectory, sessionManager.SessionId),
            extensionRuntime);

        var session = new AgentSession(
            provider,
            sessionManager,
            toolRuntime,
            options.Model,
            finalSystemPrompt,
            options.ThinkingLevel,
            options.MaxTurns,
            options.MaxRetryDelayMs,
            options.RequestHeaders,
            options.OnPayload,
            options.OnDebugLog,
            options.ThinkingBudgets,
            resourceSnapshot,
            extensionRuntime,
            options,
            baseTools.ToArray());

        if (extensionRuntime != null)
            await extensionRuntime.EmitSessionStartAsync(ct);

        return session;
    }

    public IReadOnlyList<LlmMessage> RebuildConversation() => SessionManager.RebuildContext();

    public IAsyncEnumerable<AgentEvent> PromptAsync(
        string prompt,
        CancellationToken ct = default)
        => PromptCoreAsync(prompt, ExtensionInputSource.Session, ct);

    public IAsyncEnumerable<AgentEvent> PromptAsync(
        string prompt,
        ExtensionInputSource source,
        CancellationToken ct = default)
        => PromptCoreAsync(prompt, source, ct);

    private async IAsyncEnumerable<AgentEvent> PromptCoreAsync(
        string prompt,
        ExtensionInputSource source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var effectivePrompt = prompt;

        if (_extensionRuntime != null)
        {
            if (effectivePrompt.StartsWith("/", StringComparison.Ordinal))
            {
                var commandHandled = await _extensionRuntime.TryExecuteCommandAsync(effectivePrompt, ct);
                if (commandHandled)
                    yield break;
            }

            var inputResult = await _extensionRuntime.EmitInputAsync(effectivePrompt, source, ct);
            if (inputResult.Action == ExtensionInputAction.Handled)
                yield break;
            if (inputResult.Action == ExtensionInputAction.Transform && inputResult.Text != null)
                effectivePrompt = inputResult.Text;
        }

        var effectiveSystemPrompt = SystemPrompt;
        IReadOnlyList<LlmMessage>? extensionMessages = null;
        if (_extensionRuntime != null)
        {
            var beforeAgentStart = await _extensionRuntime.EmitBeforeAgentStartAsync(
                effectivePrompt,
                effectiveSystemPrompt,
                ct);
            if (beforeAgentStart?.SystemPrompt != null)
                effectiveSystemPrompt = beforeAgentStart.SystemPrompt;
            extensionMessages = beforeAgentStart?.Messages;
        }

        var runToken = BeginRun(ct);
        try
        {
            var userMessage = LlmMessage.UserText(effectivePrompt);
            await SessionManager.AppendMessageAsync(userMessage, runToken);

            var conversation = SessionManager.RebuildContext();

            if (extensionMessages is { Count: > 0 })
            {
                conversation.AddRange(extensionMessages);
                foreach (var extensionMessage in extensionMessages)
                {
                    await SessionManager.AppendMessageAsync(extensionMessage, runToken);
                }
            }

            Func<IReadOnlyList<LlmMessage>, CancellationToken, Task<IReadOnlyList<LlmMessage>>>? transformContext = null;
            if (_extensionRuntime != null || TransformContext != null)
                transformContext = TransformConversationAsync;

            await foreach (var evt in _loop.RunControlledAsync(
                               conversation,
                               effectivePrompt,
                               isContinuation: false,
                               Model,
                               effectiveSystemPrompt,
                               ThinkingLevel,
                               MaxTurns,
                               AppendMessageAsync,
                               DequeueSteeringMessagesAsync,
                               DequeueFollowUpMessagesAsync,
                               transformContext,
                               ConvertToLlm,
                               SessionManager.SessionId,
                               _maxRetryDelayMs,
                               _requestHeaders,
                               _onPayload,
                               _thinkingBudgets,
                               _onDebugLog,
                               null, // compactionService - not yet integrated
                               runToken))
            {
                yield return evt;
            }
        }
        finally
        {
            EndRun();
        }
    }

    public async IAsyncEnumerable<AgentEvent> ContinueAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var conversation = SessionManager.RebuildContext();
        if (conversation.Count == 0)
            throw new InvalidOperationException("Cannot continue from an empty session");

        var runToken = BeginRun(ct);
        try
        {
            if (conversation[^1].Role == LlmMessageRole.Assistant)
            {
                var queuedMessages = DequeueContinuationMessages();
                if (queuedMessages.Count == 0)
                    throw new InvalidOperationException("Cannot continue when last message is assistant and queues are empty");

                conversation.AddRange(queuedMessages);
                foreach (var queued in queuedMessages)
                {
                    await AppendMessageAsync(queued, runToken);
                }
            }

            Func<IReadOnlyList<LlmMessage>, CancellationToken, Task<IReadOnlyList<LlmMessage>>>? transformContext = null;
            if (_extensionRuntime != null || TransformContext != null)
                transformContext = TransformConversationAsync;

            await foreach (var evt in _loop.RunControlledAsync(
                               conversation,
                               prompt: null,
                               isContinuation: true,
                               Model,
                               SystemPrompt,
                               ThinkingLevel,
                               MaxTurns,
                               AppendMessageAsync,
                               DequeueSteeringMessagesAsync,
                               DequeueFollowUpMessagesAsync,
                               transformContext,
                               ConvertToLlm,
                               SessionManager.SessionId,
                               _maxRetryDelayMs,
                               _requestHeaders,
                               _onPayload,
                               _thinkingBudgets,
                               _onDebugLog,
                               null, // compactionService - not yet integrated
                               runToken))
            {
                yield return evt;
            }
        }
        finally
        {
            EndRun();
        }
    }

    public void Steer(string prompt)
        => EnqueueMessage(_steeringQueue, prompt);

    public void FollowUp(string prompt)
        => EnqueueMessage(_followUpQueue, prompt);

    public void Abort()
    {
        lock (_stateLock)
        {
            _runningCts?.Cancel();
        }
    }

    public Task WaitForIdleAsync(CancellationToken ct = default)
    {
        Task? runningTask;
        lock (_stateLock)
        {
            runningTask = _runningTcs?.Task;
        }

        return runningTask == null
            ? Task.CompletedTask
            : runningTask.WaitAsync(ct);
    }

    public async Task ReloadExtensionsAsync(CancellationToken ct = default)
    {
        if (_reloadOptions == null || _baseToolsForReload == null)
        {
            throw new InvalidOperationException(
                "Extension reload is only available for sessions created through AgentSession.CreateAsync.");
        }

        await _extensionReloadLock.WaitAsync(ct);
        try
        {
            await WaitForIdleAsync(ct);

            var options = _reloadOptions;
            var baseTools = _baseToolsForReload;

            ExtensionRuntime? extensionRuntime = null;
            var extensionDiscoveryDiagnostics = new List<ExtensionDiagnostic>();

            if (options.EnableExtensions)
            {
                var extensions = new List<IAgentExtension>();
                if (options.Extensions is { Count: > 0 })
                    extensions.AddRange(options.Extensions);

                if (options.DiscoverExtensions || options.ExtensionPaths is { Count: > 0 })
                {
                    var discovered = ExtensionLoader.DiscoverAndLoad(
                        options.WorkingDirectory,
                        options.AgentDirectory,
                        options.ExtensionPaths,
                        includeDefaultDirectories: options.DiscoverExtensions);
                    extensionDiscoveryDiagnostics.AddRange(discovered.Diagnostics);
                    extensions.AddRange(discovered.Extensions);
                }

                if (extensions.Count > 0)
                {
                    extensionRuntime = await ExtensionRuntime.CreateAsync(
                        extensions,
                        options.WorkingDirectory,
                        options.ExtensionFlagValues,
                        ct);

                    foreach (var registration in extensionRuntime.ProviderFactories)
                    {
                        LlmProviderFactory.Register(
                            registration.ApiKind,
                            registration.Factory,
                            overwrite: registration.Overwrite);
                    }
                }
            }

            var extensionToolAdapters = extensionRuntime?.CreateRegisteredTools() ?? [];
            var effectiveTools = new List<IAgentTool>(baseTools.Count + extensionToolAdapters.Count);
            effectiveTools.AddRange(baseTools);
            effectiveTools.AddRange(extensionToolAdapters);

            var extensionResources = extensionRuntime == null
                ? new ExtensionResourcesDiscoverResult()
                : await extensionRuntime.EmitResourcesDiscoverAsync(ExtensionResourcesDiscoverReason.Reload, ct);
            var mergedSkillPaths = MergePaths(options.SkillPaths, extensionResources.SkillPaths);

            var resourceLoader = new SessionResourceLoader();
            var resourceLoadResult = resourceLoader.Load(new SessionResourceOptions(
                WorkingDirectory: options.WorkingDirectory,
                AgentDirectory: options.AgentDirectory,
                BaseSystemPrompt: options.SystemPrompt,
                AppendSystemPrompt: options.AppendSystemPrompt,
                DiscoverSystemPromptFile: options.DiscoverSystemPromptFile,
                IncludeProjectContextFiles: options.IncludeProjectContextFiles,
                EnableSkills: options.EnableSkills,
                IncludeDefaultSkills: options.IncludeDefaultSkills,
                SkillPaths: mergedSkillPaths));

            var resourceDiagnostics = BuildResourceDiagnostics(
                resourceLoadResult.Diagnostics,
                extensionDiscoveryDiagnostics,
                extensionRuntime,
                extensionResources);

            var includeSkillsInPrompt = effectiveTools.Any(x => string.Equals(x.Name, "read", StringComparison.Ordinal));
            var finalSystemPrompt = SystemPromptBuilder.Build(
                resourceLoadResult.BaseSystemPrompt,
                resourceLoadResult.AppendSystemPromptSections,
                resourceLoadResult.ContextFiles,
                resourceLoadResult.Skills,
                includeSkillsInPrompt);

            var resourceSnapshot = new SessionResourceSnapshot(
                BaseSystemPrompt: resourceLoadResult.BaseSystemPrompt,
                FinalSystemPrompt: finalSystemPrompt,
                AppendSystemPromptSections: resourceLoadResult.AppendSystemPromptSections,
                ContextFiles: resourceLoadResult.ContextFiles,
                Skills: resourceLoadResult.Skills,
                SkillDiagnostics: resourceLoadResult.SkillDiagnostics,
                Diagnostics: resourceDiagnostics);

            var previousRuntime = _extensionRuntime;
            if (previousRuntime != null)
                await previousRuntime.EmitSessionShutdownAsync(ct);

            _extensionRuntime = extensionRuntime;
            _extensionRuntime?.BindHost(this);

            ToolRuntime = new ToolRuntime(
                effectiveTools,
                new ToolExecutionContext(options.WorkingDirectory, SessionManager.SessionId),
                _extensionRuntime);
            _loop = new AgentLoop(_provider, ToolRuntime);

            SystemPrompt = finalSystemPrompt;
            _resourceSnapshot = resourceSnapshot;

            if (_extensionRuntime != null)
                await _extensionRuntime.EmitSessionStartAsync(ct);
        }
        finally
        {
            _extensionReloadLock.Release();
        }
    }

    public async Task<bool> RequestSessionSwitchAsync(
        ExtensionSessionSwitchReason reason,
        string? targetSessionId = null,
        CancellationToken ct = default)
    {
        if (_extensionRuntime == null)
            return true;

        var beforeSwitch = await _extensionRuntime.EmitSessionBeforeSwitchAsync(
            new ExtensionSessionBeforeSwitchEvent(SessionManager.SessionId, reason, targetSessionId),
            ct);

        return beforeSwitch?.Cancel != true;
    }

    public async Task NotifySessionSwitchedAsync(
        ExtensionSessionSwitchReason reason,
        string? previousSessionId = null,
        string? targetSessionId = null,
        CancellationToken ct = default)
    {
        if (_extensionRuntime == null)
            return;

        await _extensionRuntime.EmitSessionSwitchAsync(
            new ExtensionSessionSwitchEvent(
                SessionManager.SessionId,
                reason,
                previousSessionId,
                targetSessionId),
            ct);
    }

    public async Task<bool> ForkBranchAsync(string entryId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);

        var previousLeafId = SessionManager.CurrentLeafId;

        if (_extensionRuntime != null)
        {
            var beforeFork = await _extensionRuntime.EmitSessionBeforeForkAsync(
                new ExtensionSessionBeforeForkEvent(SessionManager.SessionId, entryId),
                ct);
            if (beforeFork?.Cancel == true)
                return false;
        }

        SessionManager.SwitchLeaf(entryId);

        if (_extensionRuntime != null)
        {
            await _extensionRuntime.EmitSessionForkAsync(
                new ExtensionSessionForkEvent(
                    SessionManager.SessionId,
                    entryId,
                    previousLeafId,
                    SessionManager.CurrentLeafId),
                ct);
        }

        return true;
    }

    public async Task<bool> NavigateTreeAsync(
        string targetEntryId,
        bool summarize = false,
        string? customInstructions = null,
        bool replaceInstructions = false,
        string? label = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetEntryId);

        var previousLeafId = SessionManager.CurrentLeafId;
        var request = new ExtensionSessionBeforeTreeEvent(
            SessionManager.SessionId,
            targetEntryId,
            summarize,
            customInstructions,
            replaceInstructions,
            label);

        if (_extensionRuntime != null)
        {
            var beforeTree = await _extensionRuntime.EmitSessionBeforeTreeAsync(request, ct);

            if (beforeTree?.Cancel == true)
                return false;

            if (beforeTree != null)
                request = beforeTree.Event;
        }

        SessionManager.SwitchLeaf(request.TargetEntryId);

        if (_extensionRuntime != null)
        {
            await _extensionRuntime.EmitSessionTreeAsync(
                new ExtensionSessionTreeEvent(
                    SessionManager.SessionId,
                    request.TargetEntryId,
                    previousLeafId,
                    SessionManager.CurrentLeafId,
                    request.Summarize,
                    request.CustomInstructions,
                    request.ReplaceInstructions,
                    request.Label),
                ct);
        }

        return true;
    }

    public Task<bool> SwitchBranchAsync(string leafEntryId, CancellationToken ct = default)
        => NavigateTreeAsync(leafEntryId, ct: ct);

    public void SwitchBranch(string leafEntryId)
        => SwitchBranchAsync(leafEntryId).GetAwaiter().GetResult();

    public async Task<SessionEntryEnvelope?> AppendCompactionAsync(
        string summary,
        string firstKeptEntryId,
        int tokensBefore,
        System.Text.Json.JsonElement? details = null,
        bool fromHook = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstKeptEntryId);

        var request = new ExtensionSessionBeforeCompactEvent(
            SessionManager.SessionId,
            summary,
            firstKeptEntryId,
            tokensBefore,
            details,
            fromHook);

        if (_extensionRuntime != null)
        {
            var beforeCompact = await _extensionRuntime.EmitSessionBeforeCompactAsync(request, ct);
            if (beforeCompact?.Cancel == true)
                return null;

            if (beforeCompact != null)
                request = beforeCompact.Event;
        }

        var entry = await SessionManager.AppendCompactionAsync(
            request.Summary,
            request.FirstKeptEntryId,
            request.TokensBefore,
            request.Details,
            request.FromHook,
            ct);

        if (_extensionRuntime != null)
        {
            await _extensionRuntime.EmitSessionCompactAsync(
                new ExtensionSessionCompactEvent(
                    SessionManager.SessionId,
                    entry.Id,
                    request.Summary,
                    request.FirstKeptEntryId,
                    request.TokensBefore,
                    request.Details,
                    request.FromHook),
                ct);
        }

        return entry;
    }

    private Task AppendMessageAsync(LlmMessage message, CancellationToken ct)
        => SessionManager.AppendMessageAsync(message, ct);

    private async Task<IReadOnlyList<LlmMessage>> TransformConversationAsync(
        IReadOnlyList<LlmMessage> conversation,
        CancellationToken ct)
    {
        var current = conversation;

        if (_extensionRuntime != null)
            current = await _extensionRuntime.EmitContextAsync(current, ct);

        if (TransformContext != null)
            current = await TransformContext(current, ct);

        return current;
    }

    private CancellationToken BeginRun(CancellationToken ct)
    {
        lock (_stateLock)
        {
            if (_runningTcs is { Task.IsCompleted: false })
                throw new InvalidOperationException("Session is already running. Use Steer/FollowUp or WaitForIdleAsync().");

            _runningTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _runningCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            return _runningCts.Token;
        }
    }

    private void EndRun()
    {
        lock (_stateLock)
        {
            _runningCts?.Dispose();
            _runningCts = null;
            _runningTcs?.TrySetResult(true);
            _runningTcs = null;
        }
    }

    private void EnqueueMessage(Queue<LlmMessage> queue, string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt cannot be empty", nameof(prompt));

        lock (_stateLock)
        {
            queue.Enqueue(LlmMessage.UserText(prompt));
        }
    }

    private Task<IReadOnlyList<LlmMessage>> DequeueSteeringMessagesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            return Task.FromResult(DequeueMessagesByMode(_steeringQueue, SteeringMode));
        }
    }

    private Task<IReadOnlyList<LlmMessage>> DequeueFollowUpMessagesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            return Task.FromResult(DequeueMessagesByMode(_followUpQueue, FollowUpMode));
        }
    }

    private static IReadOnlyList<LlmMessage> DequeueMessagesByMode(Queue<LlmMessage> queue, QueueDeliveryMode mode)
    {
        if (queue.Count == 0)
            return Array.Empty<LlmMessage>();

        if (mode == QueueDeliveryMode.OneAtATime)
            return [queue.Dequeue()];

        var list = new List<LlmMessage>(queue.Count);
        while (queue.TryDequeue(out var message))
            list.Add(message);

        return list;
    }

    private IReadOnlyList<LlmMessage> DequeueContinuationMessages()
    {
        lock (_stateLock)
        {
            if (_steeringQueue.Count > 0)
                return DequeueMessagesByMode(_steeringQueue, SteeringMode);

            if (_followUpQueue.Count > 0)
                return DequeueMessagesByMode(_followUpQueue, FollowUpMode);

            return Array.Empty<LlmMessage>();
        }
    }

    private static List<ResourceDiagnostic> BuildResourceDiagnostics(
        IReadOnlyList<ResourceDiagnostic> baseDiagnostics,
        IReadOnlyList<ExtensionDiagnostic> extensionDiscoveryDiagnostics,
        ExtensionRuntime? extensionRuntime,
        ExtensionResourcesDiscoverResult extensionResources)
    {
        var resourceDiagnostics = new List<ResourceDiagnostic>(baseDiagnostics);

        foreach (var diagnostic in extensionDiscoveryDiagnostics)
        {
            resourceDiagnostics.Add(new ResourceDiagnostic(
                diagnostic.Severity == ExtensionDiagnosticSeverity.Error
                    ? ResourceDiagnosticSeverity.Error
                    : ResourceDiagnosticSeverity.Warning,
                diagnostic.Message,
                diagnostic.ExtensionName));
        }

        if (extensionRuntime != null)
        {
            foreach (var diagnostic in extensionRuntime.Diagnostics)
            {
                resourceDiagnostics.Add(new ResourceDiagnostic(
                    diagnostic.Severity == ExtensionDiagnosticSeverity.Error
                        ? ResourceDiagnosticSeverity.Error
                        : ResourceDiagnosticSeverity.Warning,
                    diagnostic.Message,
                    diagnostic.ExtensionName));
            }
        }

        if (extensionResources.PromptPaths is { Count: > 0 })
        {
            resourceDiagnostics.Add(new ResourceDiagnostic(
                ResourceDiagnosticSeverity.Warning,
                "Extension prompt paths are not supported in this Sharp.Core phase and were ignored."));
        }

        if (extensionResources.ThemePaths is { Count: > 0 })
        {
            resourceDiagnostics.Add(new ResourceDiagnostic(
                ResourceDiagnosticSeverity.Warning,
                "Extension theme paths are not supported in this Sharp.Core phase and were ignored."));
        }

        return resourceDiagnostics;
    }

    private static IReadOnlyList<string>? MergePaths(
        IReadOnlyList<string>? basePaths,
        IReadOnlyList<string>? extensionPaths)
    {
        if (basePaths == null && extensionPaths == null)
            return null;

        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (basePaths != null)
        {
            foreach (var path in basePaths)
            {
                if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
                    merged.Add(path);
            }
        }

        if (extensionPaths != null)
        {
            foreach (var path in extensionPaths)
            {
                if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
                    merged.Add(path);
            }
        }

        return merged;
    }

    private int PendingMessageCount
    {
        get
        {
            lock (_stateLock)
                return _steeringQueue.Count + _followUpQueue.Count;
        }
    }

    public void Dispose()
    {
        Abort();
        if (_extensionRuntime != null)
        {
            try
            {
                _extensionRuntime.EmitSessionShutdownAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore extension shutdown failures during disposal.
            }
        }

        _provider.Dispose();
        _extensionReloadLock.Dispose();
    }

    string IExtensionRuntimeHost.WorkingDirectory => _workingDirectory;

    SessionManager IExtensionRuntimeHost.SessionManager => SessionManager;

    bool IExtensionRuntimeHost.IsStreaming => IsStreaming;

    bool IExtensionRuntimeHost.HasPendingMessages => PendingMessageCount > 0;

    string IExtensionRuntimeHost.SystemPrompt => SystemPrompt;

    Task IExtensionRuntimeHost.WaitForIdleAsync(CancellationToken ct) => WaitForIdleAsync(ct);

    void IExtensionRuntimeHost.Steer(string prompt) => Steer(prompt);

    void IExtensionRuntimeHost.FollowUp(string prompt) => FollowUp(prompt);
}
