namespace SharpAgent.Core;

public enum Role { System, User, Assistant, Tool }

public sealed record Message(
    Role Role,
    string Content,
    string? ToolName = null,
    string? ToolCallId = null,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? Thinking = null);

public sealed record ToolCall(string Id, string Name, string Arguments);

public sealed record LlmResponse(string? Content, string? Thinking = null, IReadOnlyList<ToolCall>? ToolCalls = null)
{
    public bool HasToolCalls => ToolCalls is { Count: > 0 };
}
