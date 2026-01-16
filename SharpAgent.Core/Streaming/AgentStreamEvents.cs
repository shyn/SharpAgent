namespace SharpAgent.Core.Streaming;

public abstract record AgentStreamEvent;

public sealed record AgentStartedEvent(string Goal) : AgentStreamEvent;
public sealed record AgentTextDeltaEvent(string Text) : AgentStreamEvent;
public sealed record AgentToolUseStartedEvent(string ToolCallId, string ToolName) : AgentStreamEvent;
public sealed record AgentToolUseArgumentsDeltaEvent(string ToolCallId, string PartialJson) : AgentStreamEvent;
public sealed record AgentToolUseCompletedEvent(string ToolCallId) : AgentStreamEvent;
public sealed record AgentToolCallStartedEvent(string ToolCallId, string ToolName, string Arguments) : AgentStreamEvent;
public sealed record AgentToolCallCompletedEvent(string ToolCallId, string Result, bool IsError) : AgentStreamEvent;
public sealed record AgentCompletedEvent(string FinalAnswer) : AgentStreamEvent;
public sealed record AgentErrorEvent(string Message) : AgentStreamEvent;
