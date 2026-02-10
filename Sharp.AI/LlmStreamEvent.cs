namespace Sharp.AI;

public abstract record LlmStreamEvent;

public sealed record LlmThinkingStartedEvent : LlmStreamEvent;

public sealed record LlmThinkingDeltaEvent(string Delta) : LlmStreamEvent;

public sealed record LlmThinkingCompletedEvent(string FullThinking, string? Signature = null) : LlmStreamEvent;

public sealed record LlmTextDeltaEvent(string Delta) : LlmStreamEvent;

public sealed record LlmToolUseStartedEvent(string ToolCallId, string ToolName) : LlmStreamEvent;

public sealed record LlmToolUseArgumentsDeltaEvent(string ToolCallId, string PartialArgumentsJson) : LlmStreamEvent;

public sealed record LlmToolUseCompletedEvent(string ToolCallId) : LlmStreamEvent;

public sealed record LlmCompletedEvent(
    string? FullText,
    string? FullThinking,
    IReadOnlyList<ToolCall> ToolCalls,
    Usage? Usage = null,
    string? ThinkingSignature = null) : LlmStreamEvent;

public sealed record LlmErrorEvent(
    string Message,
    LlmErrorCategory Category = LlmErrorCategory.Unknown,
    int? StatusCode = null,
    bool Retryable = false) : LlmStreamEvent;
