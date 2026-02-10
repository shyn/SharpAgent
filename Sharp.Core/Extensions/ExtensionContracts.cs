using System.Text.Json;
using Sharp.AI;
using Sharp.Core.Sessions;

namespace Sharp.Core.Extensions;

public enum ExtensionInputSource
{
    Session,
    Extension,
    Rpc,
    Interactive
}

public enum ExtensionInputAction
{
    Continue,
    Transform,
    Handled
}

public enum ExtensionFlagType
{
    Boolean,
    String
}

public enum ExtensionResourcesDiscoverReason
{
    Startup,
    Reload
}

public enum ExtensionSessionSwitchReason
{
    New,
    Resume,
    Manual
}

public enum ExtensionDiagnosticSeverity
{
    Warning,
    Error
}

public sealed record ExtensionDiagnostic(
    ExtensionDiagnosticSeverity Severity,
    string Message,
    string? ExtensionName = null);

public sealed record ExtensionError(
    string ExtensionName,
    string EventName,
    string Message,
    Exception? Exception = null);

public sealed record ExtensionInputEvent(
    string Text,
    ExtensionInputSource Source);

public sealed record ExtensionInputResult(
    ExtensionInputAction Action,
    string? Text = null)
{
    public static ExtensionInputResult Continue() => new(ExtensionInputAction.Continue);

    public static ExtensionInputResult Handled() => new(ExtensionInputAction.Handled);

    public static ExtensionInputResult Transform(string text)
        => new(ExtensionInputAction.Transform, text);
}

public sealed record ExtensionContextEvent(
    IReadOnlyList<LlmMessage> Messages);

public sealed record ExtensionContextResult(
    IReadOnlyList<LlmMessage>? Messages = null);

public sealed record ExtensionBeforeAgentStartEvent(
    string Prompt,
    string SystemPrompt);

public sealed record ExtensionBeforeAgentStartResult(
    IReadOnlyList<LlmMessage>? Messages = null,
    string? SystemPrompt = null);

public sealed record ExtensionSessionStartEvent(
    string SessionId);

public sealed record ExtensionSessionShutdownEvent(
    string SessionId);

public sealed record ExtensionSessionBeforeSwitchEvent(
    string SessionId,
    ExtensionSessionSwitchReason Reason,
    string? TargetSessionId = null);

public sealed record ExtensionSessionBeforeSwitchResult(
    bool Cancel = false);

public sealed record ExtensionSessionSwitchEvent(
    string SessionId,
    ExtensionSessionSwitchReason Reason,
    string? PreviousSessionId = null,
    string? TargetSessionId = null);

public sealed record ExtensionSessionBeforeForkEvent(
    string SessionId,
    string EntryId);

public sealed record ExtensionSessionBeforeForkResult(
    bool Cancel = false);

public sealed record ExtensionSessionForkEvent(
    string SessionId,
    string EntryId,
    string? PreviousLeafId,
    string? NewLeafId);

public sealed record ExtensionSessionBeforeTreeEvent(
    string SessionId,
    string TargetEntryId,
    bool Summarize = false,
    string? CustomInstructions = null,
    bool ReplaceInstructions = false,
    string? Label = null);

public sealed record ExtensionSessionBeforeTreeResult(
    bool Cancel = false,
    string? TargetEntryId = null,
    bool? Summarize = null,
    string? CustomInstructions = null,
    bool? ReplaceInstructions = null,
    string? Label = null)
{
    public ExtensionSessionBeforeTreeEvent ApplyTo(ExtensionSessionBeforeTreeEvent original)
        => original with
        {
            TargetEntryId = TargetEntryId ?? original.TargetEntryId,
            Summarize = Summarize ?? original.Summarize,
            CustomInstructions = CustomInstructions ?? original.CustomInstructions,
            ReplaceInstructions = ReplaceInstructions ?? original.ReplaceInstructions,
            Label = Label ?? original.Label
        };
}

public sealed record ExtensionSessionBeforeTreeDecision(
    bool Cancel,
    ExtensionSessionBeforeTreeEvent Event);

public sealed record ExtensionSessionTreeEvent(
    string SessionId,
    string TargetEntryId,
    string? PreviousLeafId,
    string? NewLeafId,
    bool Summarize = false,
    string? CustomInstructions = null,
    bool ReplaceInstructions = false,
    string? Label = null);

public sealed record ExtensionSessionBeforeCompactEvent(
    string SessionId,
    string Summary,
    string FirstKeptEntryId,
    int TokensBefore,
    JsonElement? Details = null,
    bool FromHook = false);

public sealed record ExtensionSessionBeforeCompactResult(
    bool Cancel = false,
    string? Summary = null,
    string? FirstKeptEntryId = null,
    int? TokensBefore = null,
    bool ReplaceDetails = false,
    JsonElement? Details = null,
    bool? FromHook = null)
{
    public ExtensionSessionBeforeCompactEvent ApplyTo(ExtensionSessionBeforeCompactEvent original)
        => original with
        {
            Summary = Summary ?? original.Summary,
            FirstKeptEntryId = FirstKeptEntryId ?? original.FirstKeptEntryId,
            TokensBefore = TokensBefore ?? original.TokensBefore,
            Details = ReplaceDetails ? Details : original.Details,
            FromHook = FromHook ?? original.FromHook
        };
}

public sealed record ExtensionSessionBeforeCompactDecision(
    bool Cancel,
    ExtensionSessionBeforeCompactEvent Event);

public sealed record ExtensionSessionCompactEvent(
    string SessionId,
    string EntryId,
    string Summary,
    string FirstKeptEntryId,
    int TokensBefore,
    JsonElement? Details = null,
    bool FromHook = false);

public sealed record ExtensionResourcesDiscoverEvent(
    string WorkingDirectory,
    ExtensionResourcesDiscoverReason Reason);

public sealed record ExtensionResourcesDiscoverResult(
    IReadOnlyList<string>? SkillPaths = null,
    IReadOnlyList<string>? PromptPaths = null,
    IReadOnlyList<string>? ThemePaths = null);

public sealed record ExtensionToolCallEvent(
    string ToolCallId,
    string ToolName,
    JsonElement Input);

public sealed record ExtensionToolCallDecision(
    bool Block = false,
    string? Reason = null);

public sealed record ExtensionToolResultEvent(
    string ToolCallId,
    string ToolName,
    JsonElement Input,
    ToolInvocationResult Result);

public sealed record ExtensionToolResultPatch(
    bool? IsError = null,
    IReadOnlyList<ContentBlock>? Content = null,
    bool ReplaceDetails = false,
    JsonElement? Details = null)
{
    public ToolInvocationResult ApplyTo(ToolInvocationResult original)
    {
        var isError = IsError ?? original.IsError;
        var content = Content ?? original.Content;
        var details = ReplaceDetails ? Details : original.Details;
        return new ToolInvocationResult(isError, content, details);
    }

    public static ExtensionToolResultPatch WithDetails(JsonElement? details)
        => new(ReplaceDetails: true, Details: details);
}

public sealed record ExtensionFlagDefinition(
    string Name,
    ExtensionFlagType Type,
    string? Description = null,
    string? DefaultValue = null);

public sealed record ExtensionCommandDefinition(
    string Name,
    Func<string, ExtensionCommandContext, CancellationToken, ValueTask> Handler,
    string? Description = null);

public sealed record ExtensionToolDefinition(
    string Name,
    string Description,
    JsonElement ParametersSchema,
    Func<JsonElement, ExtensionContext, IProgress<ToolInvocationResult>?, CancellationToken, ValueTask<ToolInvocationResult>> ExecuteAsync);

public sealed record ExtensionProviderFactoryRegistration(
    ProviderApiKind ApiKind,
    Func<LlmProviderCreateContext, ILlmProvider> Factory,
    bool Overwrite = true);

public delegate ValueTask<ExtensionInputResult?> ExtensionInputHandler(
    ExtensionInputEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public delegate ValueTask<ExtensionContextResult?> ExtensionContextHandler(
    ExtensionContextEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public delegate ValueTask<ExtensionBeforeAgentStartResult?> ExtensionBeforeAgentStartHandler(
    ExtensionBeforeAgentStartEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public delegate ValueTask<ExtensionToolCallDecision?> ExtensionToolCallHandler(
    ExtensionToolCallEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public delegate ValueTask<ExtensionToolResultPatch?> ExtensionToolResultHandler(
    ExtensionToolResultEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public delegate ValueTask ExtensionSessionStartHandler(
    ExtensionSessionStartEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public delegate ValueTask<ExtensionSessionBeforeSwitchResult?> ExtensionSessionBeforeSwitchHandler(
    ExtensionSessionBeforeSwitchEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public delegate ValueTask ExtensionSessionSwitchHandler(
    ExtensionSessionSwitchEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public delegate ValueTask<ExtensionSessionBeforeForkResult?> ExtensionSessionBeforeForkHandler(
    ExtensionSessionBeforeForkEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public delegate ValueTask ExtensionSessionForkHandler(
    ExtensionSessionForkEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public delegate ValueTask<ExtensionSessionBeforeTreeResult?> ExtensionSessionBeforeTreeHandler(
    ExtensionSessionBeforeTreeEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public delegate ValueTask ExtensionSessionTreeHandler(
    ExtensionSessionTreeEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public delegate ValueTask<ExtensionSessionBeforeCompactResult?> ExtensionSessionBeforeCompactHandler(
    ExtensionSessionBeforeCompactEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public delegate ValueTask ExtensionSessionCompactHandler(
    ExtensionSessionCompactEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public delegate ValueTask ExtensionSessionShutdownHandler(
    ExtensionSessionShutdownEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public delegate ValueTask<ExtensionResourcesDiscoverResult?> ExtensionResourcesDiscoverHandler(
    ExtensionResourcesDiscoverEvent @event,
    ExtensionContext context,
    CancellationToken ct);

public class ExtensionContext
{
    private readonly Func<bool> _isStreaming;
    private readonly Func<bool> _hasPendingMessages;
    private readonly Func<string> _getSystemPrompt;
    private readonly IReadOnlyDictionary<string, string> _flagValues;

    internal ExtensionContext(
        string workingDirectory,
        SessionManager? sessionManager,
        Func<bool> isStreaming,
        Func<bool> hasPendingMessages,
        Func<string> getSystemPrompt,
        IReadOnlyDictionary<string, string> flagValues)
    {
        WorkingDirectory = workingDirectory;
        SessionManager = sessionManager;
        _isStreaming = isStreaming;
        _hasPendingMessages = hasPendingMessages;
        _getSystemPrompt = getSystemPrompt;
        _flagValues = flagValues;
    }

    public string WorkingDirectory { get; }

    public SessionManager? SessionManager { get; }

    public string SessionId => SessionManager?.SessionId ?? string.Empty;

    public bool IsStreaming => _isStreaming();

    public bool HasPendingMessages => _hasPendingMessages();

    public string SystemPrompt => _getSystemPrompt();

    public string? GetFlag(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _flagValues.TryGetValue(name, out var value) ? value : null;
    }

    public IReadOnlyDictionary<string, string> GetAllFlags() => _flagValues;
}

public sealed class ExtensionCommandContext : ExtensionContext
{
    private readonly Func<CancellationToken, Task> _waitForIdleAsync;
    private readonly Action<string> _steer;
    private readonly Action<string> _followUp;

    internal ExtensionCommandContext(
        string workingDirectory,
        SessionManager? sessionManager,
        Func<bool> isStreaming,
        Func<bool> hasPendingMessages,
        Func<string> getSystemPrompt,
        IReadOnlyDictionary<string, string> flagValues,
        Func<CancellationToken, Task> waitForIdleAsync,
        Action<string> steer,
        Action<string> followUp)
        : base(
            workingDirectory,
            sessionManager,
            isStreaming,
            hasPendingMessages,
            getSystemPrompt,
            flagValues)
    {
        _waitForIdleAsync = waitForIdleAsync;
        _steer = steer;
        _followUp = followUp;
    }

    public ValueTask WaitForIdleAsync(CancellationToken ct = default)
        => new(_waitForIdleAsync(ct));

    public void Steer(string prompt) => _steer(prompt);

    public void FollowUp(string prompt) => _followUp(prompt);
}
