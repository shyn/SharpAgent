namespace SharpAgent.Core.Streaming;

public sealed record LlmTextDelta(string Text);
public sealed record LlmToolUseStarted(string ToolCallId, string ToolName);
public sealed record LlmToolUseArgumentsDelta(string ToolCallId, string PartialJson);
public sealed record LlmToolUseCompleted(string ToolCallId, string Arguments);
public sealed record LlmMessageCompleted(string? FullText, IReadOnlyList<ToolCall>? ToolCalls);

public sealed record ToolCallStarted(string ToolCallId, string ToolName, string Arguments);
public sealed record ToolCallCompleted(string ToolCallId, string Result, bool IsError);

public sealed record AgentStarted(string Goal);
public sealed record AgentCompleted(string FinalAnswer);
public sealed record AgentError(string Message);
