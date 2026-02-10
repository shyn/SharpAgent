using System.Collections.ObjectModel;
using Sharp.AI;
using Sharp.Core.Sessions;

namespace Sharp.Core.Extensions;

internal interface IExtensionRuntimeHost
{
    string WorkingDirectory { get; }

    SessionManager SessionManager { get; }

    bool IsStreaming { get; }

    bool HasPendingMessages { get; }

    string SystemPrompt { get; }

    Task WaitForIdleAsync(CancellationToken ct);

    void Steer(string prompt);

    void FollowUp(string prompt);
}

public sealed class ExtensionRuntime
{
    private sealed class LoadedExtension
    {
        public required string Name { get; init; }

        public Dictionary<string, ExtensionToolDefinition> Tools { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, ExtensionCommandDefinition> Commands { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, ExtensionFlagDefinition> Flags { get; } = new(StringComparer.Ordinal);

        public List<ExtensionProviderFactoryRegistration> ProviderFactories { get; } = [];

        public List<ExtensionInputHandler> InputHandlers { get; } = [];

        public List<ExtensionContextHandler> ContextHandlers { get; } = [];

        public List<ExtensionBeforeAgentStartHandler> BeforeAgentStartHandlers { get; } = [];

        public List<ExtensionToolCallHandler> ToolCallHandlers { get; } = [];

        public List<ExtensionToolResultHandler> ToolResultHandlers { get; } = [];

        public List<ExtensionSessionStartHandler> SessionStartHandlers { get; } = [];

        public List<ExtensionSessionBeforeSwitchHandler> SessionBeforeSwitchHandlers { get; } = [];

        public List<ExtensionSessionSwitchHandler> SessionSwitchHandlers { get; } = [];

        public List<ExtensionSessionBeforeForkHandler> SessionBeforeForkHandlers { get; } = [];

        public List<ExtensionSessionForkHandler> SessionForkHandlers { get; } = [];

        public List<ExtensionSessionBeforeTreeHandler> SessionBeforeTreeHandlers { get; } = [];

        public List<ExtensionSessionTreeHandler> SessionTreeHandlers { get; } = [];

        public List<ExtensionSessionBeforeCompactHandler> SessionBeforeCompactHandlers { get; } = [];

        public List<ExtensionSessionCompactHandler> SessionCompactHandlers { get; } = [];

        public List<ExtensionSessionShutdownHandler> SessionShutdownHandlers { get; } = [];

        public List<ExtensionResourcesDiscoverHandler> ResourcesDiscoverHandlers { get; } = [];
    }

    private sealed class ExtensionApi : IAgentExtensionApi
    {
        private readonly ExtensionRuntime _runtime;
        private readonly LoadedExtension _extension;

        public ExtensionApi(ExtensionRuntime runtime, LoadedExtension extension)
        {
            _runtime = runtime;
            _extension = extension;
        }

        public void OnInput(ExtensionInputHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.InputHandlers.Add(handler);
        }

        public void OnContext(ExtensionContextHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.ContextHandlers.Add(handler);
        }

        public void OnBeforeAgentStart(ExtensionBeforeAgentStartHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.BeforeAgentStartHandlers.Add(handler);
        }

        public void OnToolCall(ExtensionToolCallHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.ToolCallHandlers.Add(handler);
        }

        public void OnToolResult(ExtensionToolResultHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.ToolResultHandlers.Add(handler);
        }

        public void OnSessionStart(ExtensionSessionStartHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.SessionStartHandlers.Add(handler);
        }

        public void OnSessionBeforeSwitch(ExtensionSessionBeforeSwitchHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.SessionBeforeSwitchHandlers.Add(handler);
        }

        public void OnSessionSwitch(ExtensionSessionSwitchHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.SessionSwitchHandlers.Add(handler);
        }

        public void OnSessionBeforeFork(ExtensionSessionBeforeForkHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.SessionBeforeForkHandlers.Add(handler);
        }

        public void OnSessionFork(ExtensionSessionForkHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.SessionForkHandlers.Add(handler);
        }

        public void OnSessionBeforeTree(ExtensionSessionBeforeTreeHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.SessionBeforeTreeHandlers.Add(handler);
        }

        public void OnSessionTree(ExtensionSessionTreeHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.SessionTreeHandlers.Add(handler);
        }

        public void OnSessionBeforeCompact(ExtensionSessionBeforeCompactHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.SessionBeforeCompactHandlers.Add(handler);
        }

        public void OnSessionCompact(ExtensionSessionCompactHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.SessionCompactHandlers.Add(handler);
        }

        public void OnSessionShutdown(ExtensionSessionShutdownHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.SessionShutdownHandlers.Add(handler);
        }

        public void OnResourcesDiscover(ExtensionResourcesDiscoverHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _extension.ResourcesDiscoverHandlers.Add(handler);
        }

        public void RegisterTool(ExtensionToolDefinition tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            if (string.IsNullOrWhiteSpace(tool.Name))
                throw new InvalidOperationException("Extension tool name cannot be empty");

            _extension.Tools[tool.Name] = tool;
        }

        public void RegisterCommand(ExtensionCommandDefinition command)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (string.IsNullOrWhiteSpace(command.Name))
                throw new InvalidOperationException("Extension command name cannot be empty");

            _extension.Commands[command.Name] = command;
        }

        public void RegisterFlag(ExtensionFlagDefinition flag)
        {
            ArgumentNullException.ThrowIfNull(flag);
            if (string.IsNullOrWhiteSpace(flag.Name))
                throw new InvalidOperationException("Extension flag name cannot be empty");

            _extension.Flags[flag.Name] = flag;
        }

        public string? GetFlag(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            if (!_extension.Flags.TryGetValue(name, out var flag))
                return null;

            if (_runtime._providedFlagValues.TryGetValue(name, out var provided))
                return provided;

            return flag.DefaultValue;
        }

        public void RegisterProviderFactory(ProviderApiKind apiKind, Func<LlmProviderCreateContext, ILlmProvider> factory, bool overwrite = true)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _extension.ProviderFactories.Add(new ExtensionProviderFactoryRegistration(apiKind, factory, overwrite));
        }
    }

    private sealed class ExtensionAgentToolAdapter : IAgentTool
    {
        private readonly ExtensionToolDefinition _definition;
        private readonly Func<ExtensionContext> _contextFactory;

        public ExtensionAgentToolAdapter(ExtensionToolDefinition definition, Func<ExtensionContext> contextFactory)
        {
            _definition = definition;
            _contextFactory = contextFactory;
        }

        public string Name => _definition.Name;

        public string Description => _definition.Description;

        public System.Text.Json.JsonElement ParametersSchema => _definition.ParametersSchema;

        public Task<ToolInvocationResult> ExecuteAsync(
            System.Text.Json.JsonElement arguments,
            ToolExecutionContext context,
            IProgress<ToolInvocationResult>? progress = null,
            CancellationToken ct = default)
            => _definition.ExecuteAsync(arguments, _contextFactory(), progress, ct).AsTask();
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyFlags =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    private readonly List<LoadedExtension> _extensions = [];
    private readonly Dictionary<string, string> _providedFlagValues;
    private readonly Dictionary<string, string> _resolvedFlagValues = new(StringComparer.Ordinal);
    private readonly List<ExtensionDiagnostic> _diagnostics = [];
    private readonly List<ExtensionProviderFactoryRegistration> _providerFactories = [];
    private readonly object _errorListenersLock = new();
    private readonly List<Action<ExtensionError>> _errorListeners = [];
    private readonly string _initialWorkingDirectory;

    private IExtensionRuntimeHost? _host;

    private ExtensionRuntime(
        string workingDirectory,
        IReadOnlyDictionary<string, string>? providedFlagValues)
    {
        _initialWorkingDirectory = workingDirectory;
        _providedFlagValues = providedFlagValues == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(providedFlagValues, StringComparer.Ordinal);
    }

    public static async Task<ExtensionRuntime> CreateAsync(
        IEnumerable<IAgentExtension>? extensions,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? providedFlagValues = null,
        CancellationToken ct = default)
    {
        var runtime = new ExtensionRuntime(workingDirectory, providedFlagValues);

        if (extensions != null)
            await runtime.LoadAsync(extensions, ct);

        return runtime;
    }

    public IReadOnlyList<ExtensionDiagnostic> Diagnostics => _diagnostics;

    public IReadOnlyList<ExtensionProviderFactoryRegistration> ProviderFactories => _providerFactories;

    public bool HasExtensions => _extensions.Count > 0;

    public IDisposable SubscribeErrors(Action<ExtensionError> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        lock (_errorListenersLock)
            _errorListeners.Add(listener);

        return new Unsubscriber(this, listener);
    }

    internal void BindHost(IExtensionRuntimeHost host)
    {
        _host = host;
    }

    public IReadOnlyList<IAgentTool> CreateRegisteredTools()
    {
        if (_extensions.Count == 0)
            return [];

        var tools = new List<IAgentTool>();
        foreach (var extension in _extensions)
        {
            foreach (var tool in extension.Tools.Values)
                tools.Add(new ExtensionAgentToolAdapter(tool, CreateContext));
        }

        return tools;
    }

    public IReadOnlyList<ExtensionCommandDefinition> GetRegisteredCommands()
    {
        var commands = new List<ExtensionCommandDefinition>();
        foreach (var extension in _extensions)
            commands.AddRange(extension.Commands.Values);
        return commands;
    }

    public bool HasCommand(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        foreach (var extension in _extensions)
        {
            if (extension.Commands.ContainsKey(name))
                return true;
        }

        return false;
    }

    public async Task<bool> TryExecuteCommandAsync(string commandText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(commandText) || commandText[0] != '/')
            return false;

        var spaceIndex = commandText.IndexOf(' ');
        var commandName = spaceIndex < 0
            ? commandText[1..]
            : commandText[1..spaceIndex];
        var args = spaceIndex < 0 ? string.Empty : commandText[(spaceIndex + 1)..];

        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        foreach (var extension in _extensions)
        {
            if (!extension.Commands.TryGetValue(commandName, out var command))
                continue;

            if (_host == null)
            {
                EmitError(new ExtensionError(extension.Name, "command", "Extension host is not initialized"));
                return true;
            }

            try
            {
                await command.Handler(args, CreateCommandContext(), ct);
            }
            catch (Exception ex)
            {
                EmitError(new ExtensionError(extension.Name, "command", ex.Message, ex));
            }

            return true;
        }

        return false;
    }

    public async Task EmitSessionStartAsync(CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return;

        var @event = new ExtensionSessionStartEvent(CreateContext().SessionId);
        var context = CreateContext();

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.SessionStartHandlers)
            {
                try
                {
                    await handler(@event, context, ct);
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "session_start", ex.Message, ex));
                }
            }
        }
    }

    public async Task<ExtensionSessionBeforeSwitchResult?> EmitSessionBeforeSwitchAsync(
        ExtensionSessionBeforeSwitchEvent @event,
        CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return null;

        var context = CreateContext();

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.SessionBeforeSwitchHandlers)
            {
                try
                {
                    var result = await handler(@event, context, ct);
                    if (result?.Cancel == true)
                        return result;
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "session_before_switch", ex.Message, ex));
                }
            }
        }

        return null;
    }

    public async Task EmitSessionSwitchAsync(
        ExtensionSessionSwitchEvent @event,
        CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return;

        var context = CreateContext();

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.SessionSwitchHandlers)
            {
                try
                {
                    await handler(@event, context, ct);
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "session_switch", ex.Message, ex));
                }
            }
        }
    }

    public async Task<ExtensionSessionBeforeForkResult?> EmitSessionBeforeForkAsync(
        ExtensionSessionBeforeForkEvent @event,
        CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return null;

        var context = CreateContext();

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.SessionBeforeForkHandlers)
            {
                try
                {
                    var result = await handler(@event, context, ct);
                    if (result?.Cancel == true)
                        return result;
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "session_before_fork", ex.Message, ex));
                }
            }
        }

        return null;
    }

    public async Task EmitSessionForkAsync(
        ExtensionSessionForkEvent @event,
        CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return;

        var context = CreateContext();

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.SessionForkHandlers)
            {
                try
                {
                    await handler(@event, context, ct);
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "session_fork", ex.Message, ex));
                }
            }
        }
    }

    public async Task<ExtensionSessionBeforeTreeDecision?> EmitSessionBeforeTreeAsync(
        ExtensionSessionBeforeTreeEvent @event,
        CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return null;

        var context = CreateContext();
        var currentEvent = @event;
        var changed = false;

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.SessionBeforeTreeHandlers)
            {
                try
                {
                    var result = await handler(currentEvent, context, ct);
                    if (result == null)
                        continue;

                    if (result.Cancel)
                        return new ExtensionSessionBeforeTreeDecision(true, result.ApplyTo(currentEvent));

                    var nextEvent = result.ApplyTo(currentEvent);
                    if (!nextEvent.Equals(currentEvent))
                        changed = true;

                    currentEvent = nextEvent;
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "session_before_tree", ex.Message, ex));
                }
            }
        }

        return changed
            ? new ExtensionSessionBeforeTreeDecision(false, currentEvent)
            : null;
    }

    public async Task EmitSessionTreeAsync(
        ExtensionSessionTreeEvent @event,
        CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return;

        var context = CreateContext();

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.SessionTreeHandlers)
            {
                try
                {
                    await handler(@event, context, ct);
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "session_tree", ex.Message, ex));
                }
            }
        }
    }

    public async Task<ExtensionSessionBeforeCompactDecision?> EmitSessionBeforeCompactAsync(
        ExtensionSessionBeforeCompactEvent @event,
        CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return null;

        var context = CreateContext();
        var currentEvent = @event;
        var changed = false;

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.SessionBeforeCompactHandlers)
            {
                try
                {
                    var result = await handler(currentEvent, context, ct);
                    if (result == null)
                        continue;

                    if (result.Cancel)
                        return new ExtensionSessionBeforeCompactDecision(true, result.ApplyTo(currentEvent));

                    var nextEvent = result.ApplyTo(currentEvent);
                    if (!nextEvent.Equals(currentEvent))
                        changed = true;

                    currentEvent = nextEvent;
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "session_before_compact", ex.Message, ex));
                }
            }
        }

        return changed
            ? new ExtensionSessionBeforeCompactDecision(false, currentEvent)
            : null;
    }

    public async Task EmitSessionCompactAsync(
        ExtensionSessionCompactEvent @event,
        CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return;

        var context = CreateContext();

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.SessionCompactHandlers)
            {
                try
                {
                    await handler(@event, context, ct);
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "session_compact", ex.Message, ex));
                }
            }
        }
    }

    public async Task EmitSessionShutdownAsync(CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return;

        var @event = new ExtensionSessionShutdownEvent(CreateContext().SessionId);
        var context = CreateContext();

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.SessionShutdownHandlers)
            {
                try
                {
                    await handler(@event, context, ct);
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "session_shutdown", ex.Message, ex));
                }
            }
        }
    }

    public async Task<ExtensionResourcesDiscoverResult> EmitResourcesDiscoverAsync(
        ExtensionResourcesDiscoverReason reason,
        CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return new ExtensionResourcesDiscoverResult();

        var context = CreateContext();
        var @event = new ExtensionResourcesDiscoverEvent(context.WorkingDirectory, reason);

        var skillPaths = new List<string>();
        var promptPaths = new List<string>();
        var themePaths = new List<string>();
        var seenSkillPaths = new HashSet<string>(StringComparer.Ordinal);
        var seenPromptPaths = new HashSet<string>(StringComparer.Ordinal);
        var seenThemePaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.ResourcesDiscoverHandlers)
            {
                try
                {
                    var result = await handler(@event, context, ct);
                    AddDistinct(result?.SkillPaths, skillPaths, seenSkillPaths);
                    AddDistinct(result?.PromptPaths, promptPaths, seenPromptPaths);
                    AddDistinct(result?.ThemePaths, themePaths, seenThemePaths);
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "resources_discover", ex.Message, ex));
                }
            }
        }

        return new ExtensionResourcesDiscoverResult(skillPaths, promptPaths, themePaths);
    }

    public async Task<ExtensionInputResult> EmitInputAsync(
        string text,
        ExtensionInputSource source,
        CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return ExtensionInputResult.Continue();

        var context = CreateContext();
        var currentText = text;
        var transformed = false;

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.InputHandlers)
            {
                try
                {
                    var result = await handler(new ExtensionInputEvent(currentText, source), context, ct);
                    if (result == null || result.Action == ExtensionInputAction.Continue)
                        continue;

                    if (result.Action == ExtensionInputAction.Handled)
                        return ExtensionInputResult.Handled();

                    if (result.Action == ExtensionInputAction.Transform)
                    {
                        currentText = result.Text ?? currentText;
                        transformed = true;
                    }
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "input", ex.Message, ex));
                }
            }
        }

        return transformed
            ? ExtensionInputResult.Transform(currentText)
            : ExtensionInputResult.Continue();
    }

    public async Task<IReadOnlyList<LlmMessage>> EmitContextAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return messages;

        var context = CreateContext();
        IReadOnlyList<LlmMessage> currentMessages = messages;

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.ContextHandlers)
            {
                try
                {
                    var result = await handler(new ExtensionContextEvent(currentMessages), context, ct);
                    if (result?.Messages != null)
                        currentMessages = result.Messages;
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "context", ex.Message, ex));
                }
            }
        }

        return currentMessages;
    }

    public async Task<ExtensionBeforeAgentStartResult?> EmitBeforeAgentStartAsync(
        string prompt,
        string systemPrompt,
        CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return null;

        var context = CreateContext();
        var currentSystemPrompt = systemPrompt;
        var messages = new List<LlmMessage>();
        var hasSystemPromptOverride = false;

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.BeforeAgentStartHandlers)
            {
                try
                {
                    var result = await handler(new ExtensionBeforeAgentStartEvent(prompt, currentSystemPrompt), context, ct);
                    if (result == null)
                        continue;

                    if (result.Messages != null)
                        messages.AddRange(result.Messages);

                    if (result.SystemPrompt != null)
                    {
                        currentSystemPrompt = result.SystemPrompt;
                        hasSystemPromptOverride = true;
                    }
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "before_agent_start", ex.Message, ex));
                }
            }
        }

        if (messages.Count == 0 && !hasSystemPromptOverride)
            return null;

        return new ExtensionBeforeAgentStartResult(
            messages.Count == 0 ? null : messages,
            hasSystemPromptOverride ? currentSystemPrompt : null);
    }

    public async Task<ExtensionToolCallDecision?> EmitToolCallAsync(ExtensionToolCallEvent @event, CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return null;

        var context = CreateContext();

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.ToolCallHandlers)
            {
                try
                {
                    var result = await handler(@event, context, ct);
                    if (result?.Block == true)
                        return result;
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "tool_call", ex.Message, ex));
                    return new ExtensionToolCallDecision(
                        Block: true,
                        Reason: $"Tool execution blocked by extension failure in '{extension.Name}': {ex.Message}");
                }
            }
        }

        return null;
    }

    public async Task<ToolInvocationResult> EmitToolResultAsync(ExtensionToolResultEvent @event, CancellationToken ct = default)
    {
        if (_extensions.Count == 0)
            return @event.Result;

        var context = CreateContext();
        var currentResult = @event.Result;

        foreach (var extension in _extensions)
        {
            foreach (var handler in extension.ToolResultHandlers)
            {
                try
                {
                    var patch = await handler(@event with { Result = currentResult }, context, ct);
                    if (patch != null)
                        currentResult = patch.ApplyTo(currentResult);
                }
                catch (Exception ex)
                {
                    EmitError(new ExtensionError(extension.Name, "tool_result", ex.Message, ex));
                }
            }
        }

        return currentResult;
    }

    private async Task LoadAsync(IEnumerable<IAgentExtension> extensions, CancellationToken ct)
    {
        foreach (var extension in extensions)
        {
            ct.ThrowIfCancellationRequested();

            var extensionName = string.IsNullOrWhiteSpace(extension.Name)
                ? extension.GetType().Name
                : extension.Name;
            var state = new LoadedExtension { Name = extensionName };
            var api = new ExtensionApi(this, state);

            try
            {
                await extension.InitializeAsync(api, ct);
                _extensions.Add(state);
            }
            catch (Exception ex)
            {
                _diagnostics.Add(new ExtensionDiagnostic(
                    ExtensionDiagnosticSeverity.Error,
                    $"Failed to initialize extension: {ex.Message}",
                    extensionName));
            }
        }

        ResolveConflicts();
        BuildResolvedFlags();
        BuildProviderFactories();
    }

    private void ResolveConflicts()
    {
        if (_extensions.Count <= 1)
            return;

        var accepted = new List<LoadedExtension>(_extensions.Count);
        var toolOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        var commandOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        var flagOwners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var extension in _extensions)
        {
            var conflicts = new List<string>();

            foreach (var toolName in extension.Tools.Keys)
            {
                if (toolOwners.TryGetValue(toolName, out var owner))
                    conflicts.Add($"Tool '{toolName}' conflicts with extension '{owner}'");
            }

            foreach (var commandName in extension.Commands.Keys)
            {
                if (commandOwners.TryGetValue(commandName, out var owner))
                    conflicts.Add($"Command '/{commandName}' conflicts with extension '{owner}'");
            }

            foreach (var flagName in extension.Flags.Keys)
            {
                if (flagOwners.TryGetValue(flagName, out var owner))
                    conflicts.Add($"Flag '--{flagName}' conflicts with extension '{owner}'");
            }

            if (conflicts.Count > 0)
            {
                foreach (var conflict in conflicts)
                {
                    _diagnostics.Add(new ExtensionDiagnostic(
                        ExtensionDiagnosticSeverity.Error,
                        conflict,
                        extension.Name));
                }
                continue;
            }

            accepted.Add(extension);

            foreach (var toolName in extension.Tools.Keys)
                toolOwners[toolName] = extension.Name;
            foreach (var commandName in extension.Commands.Keys)
                commandOwners[commandName] = extension.Name;
            foreach (var flagName in extension.Flags.Keys)
                flagOwners[flagName] = extension.Name;
        }

        _extensions.Clear();
        _extensions.AddRange(accepted);
    }

    private void BuildResolvedFlags()
    {
        _resolvedFlagValues.Clear();

        var knownFlags = new Dictionary<string, ExtensionFlagDefinition>(StringComparer.Ordinal);

        foreach (var extension in _extensions)
        {
            foreach (var flag in extension.Flags.Values)
                knownFlags[flag.Name] = flag;
        }

        foreach (var flag in knownFlags.Values)
        {
            if (flag.DefaultValue == null)
                continue;

            if (TryNormalizeFlagValue(flag, flag.DefaultValue, out var normalizedValue))
                _resolvedFlagValues[flag.Name] = normalizedValue!;
            else
                _diagnostics.Add(new ExtensionDiagnostic(
                    ExtensionDiagnosticSeverity.Error,
                    $"Invalid default value '{flag.DefaultValue}' for flag '--{flag.Name}'",
                    null));
        }

        foreach (var (name, value) in _providedFlagValues)
        {
            if (!knownFlags.TryGetValue(name, out var flag))
            {
                _diagnostics.Add(new ExtensionDiagnostic(
                    ExtensionDiagnosticSeverity.Warning,
                    $"Ignoring unknown extension flag '--{name}'",
                    null));
                continue;
            }

            if (!TryNormalizeFlagValue(flag, value, out var normalizedValue))
            {
                _diagnostics.Add(new ExtensionDiagnostic(
                    ExtensionDiagnosticSeverity.Error,
                    $"Invalid value '{value}' for flag '--{name}'",
                    null));
                continue;
            }

            _resolvedFlagValues[name] = normalizedValue!;
        }
    }

    private void BuildProviderFactories()
    {
        _providerFactories.Clear();

        foreach (var extension in _extensions)
            _providerFactories.AddRange(extension.ProviderFactories);
    }

    private static bool TryNormalizeFlagValue(ExtensionFlagDefinition flag, string rawValue, out string? normalizedValue)
    {
        normalizedValue = null;

        if (flag.Type == ExtensionFlagType.String)
        {
            normalizedValue = rawValue;
            return true;
        }

        if (!bool.TryParse(rawValue, out var parsedBool))
            return false;

        normalizedValue = parsedBool ? "true" : "false";
        return true;
    }

    private ExtensionContext CreateContext()
    {
        var host = _host;
        if (host == null)
        {
            return new ExtensionContext(
                _initialWorkingDirectory,
                sessionManager: null,
                isStreaming: static () => false,
                hasPendingMessages: static () => false,
                getSystemPrompt: static () => string.Empty,
                flagValues: _resolvedFlagValues.Count == 0
                    ? EmptyFlags
                    : new ReadOnlyDictionary<string, string>(_resolvedFlagValues));
        }

        return new ExtensionContext(
            host.WorkingDirectory,
            host.SessionManager,
            isStreaming: () => host.IsStreaming,
            hasPendingMessages: () => host.HasPendingMessages,
            getSystemPrompt: () => host.SystemPrompt,
            flagValues: _resolvedFlagValues.Count == 0
                ? EmptyFlags
                : new ReadOnlyDictionary<string, string>(_resolvedFlagValues));
    }

    private ExtensionCommandContext CreateCommandContext()
    {
        var host = _host;
        if (host == null)
        {
            return new ExtensionCommandContext(
                _initialWorkingDirectory,
                sessionManager: null,
                isStreaming: static () => false,
                hasPendingMessages: static () => false,
                getSystemPrompt: static () => string.Empty,
                flagValues: _resolvedFlagValues.Count == 0
                    ? EmptyFlags
                    : new ReadOnlyDictionary<string, string>(_resolvedFlagValues),
                waitForIdleAsync: static _ => Task.CompletedTask,
                steer: static _ => { },
                followUp: static _ => { });
        }

        return new ExtensionCommandContext(
            host.WorkingDirectory,
            host.SessionManager,
            isStreaming: () => host.IsStreaming,
            hasPendingMessages: () => host.HasPendingMessages,
            getSystemPrompt: () => host.SystemPrompt,
            flagValues: _resolvedFlagValues.Count == 0
                ? EmptyFlags
                : new ReadOnlyDictionary<string, string>(_resolvedFlagValues),
            waitForIdleAsync: host.WaitForIdleAsync,
            steer: host.Steer,
            followUp: host.FollowUp);
    }

    private void EmitError(ExtensionError error)
    {
        List<Action<ExtensionError>> listeners;
        lock (_errorListenersLock)
            listeners = [.. _errorListeners];

        foreach (var listener in listeners)
        {
            try
            {
                listener(error);
            }
            catch
            {
                // Ignore listener errors.
            }
        }
    }

    private static void AddDistinct(
        IReadOnlyList<string>? values,
        List<string> destination,
        HashSet<string> seen)
    {
        if (values == null || values.Count == 0)
            return;

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (seen.Add(value))
                destination.Add(value);
        }
    }

    private sealed class Unsubscriber : IDisposable
    {
        private readonly ExtensionRuntime _runtime;
        private readonly Action<ExtensionError> _listener;
        private bool _disposed;

        public Unsubscriber(ExtensionRuntime runtime, Action<ExtensionError> listener)
        {
            _runtime = runtime;
            _listener = listener;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            lock (_runtime._errorListenersLock)
                _runtime._errorListeners.Remove(_listener);

            _disposed = true;
        }
    }
}
