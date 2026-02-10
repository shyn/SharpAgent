using Sharp.AI;

namespace Sharp.Core;

public abstract record AgentEvent;

public sealed record AgentStartedEvent(string? Prompt, bool IsContinuation = false) : AgentEvent;

public sealed record AgentThinkingStartedEvent : AgentEvent;

public sealed record AgentThinkingDeltaEvent(string Delta) : AgentEvent;

public sealed record AgentThinkingCompletedEvent(string FullThinking) : AgentEvent;

public sealed record AgentTextDeltaEvent(string Delta) : AgentEvent;

public sealed record AgentToolUseStartedEvent(string ToolCallId, string ToolName) : AgentEvent;

public sealed record AgentToolUseArgumentsDeltaEvent(string ToolCallId, string PartialArgumentsJson) : AgentEvent;

public sealed record AgentToolUseCompletedEvent(string ToolCallId) : AgentEvent;

public sealed record AgentToolExecutionStartedEvent(string ToolCallId, string ToolName, string ArgumentsJson) : AgentEvent;

public sealed record AgentToolExecutionUpdatedEvent(string ToolCallId, string ToolName, ToolInvocationResult PartialResult) : AgentEvent;

public sealed record AgentToolExecutionCompletedEvent(string ToolCallId, string ToolName, ToolInvocationResult Result) : AgentEvent;

public sealed record AgentTurnCompletedEvent(LlmMessage AssistantMessage, IReadOnlyList<LlmMessage> ToolMessages) : AgentEvent;

public sealed record AgentCompletedEvent(LlmMessage AssistantMessage) : AgentEvent;

public sealed record AgentErrorEvent(
    string Message,
    LlmErrorCategory Category = LlmErrorCategory.Unknown,
    int? StatusCode = null,
    bool Retryable = false) : AgentEvent;

/// <summary>
/// Event raised when the conversation has grown large enough to require compaction.
/// The caller should handle this by triggering compaction through the CompactionService.
/// </summary>
/// <param name="TokenCount">Current estimated token count.</param>
/// <param name="Threshold">The token threshold that was exceeded.</param>
public sealed record AgentCompactionRequiredEvent(int TokenCount, int Threshold) : AgentEvent;
