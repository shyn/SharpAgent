namespace SharpAgent.Core;

/// <summary>
/// Represents the result of a tool execution with structured error information.
/// </summary>
public sealed record ToolResult(string Output, bool IsError = false, string? ErrorCode = null)
{
    public static ToolResult Success(string output) => new(output);
    public static ToolResult Error(string message, string? errorCode = null) => new(message, IsError: true, ErrorCode: errorCode);
}

public interface ITool
{
    string Name { get; }
    string Description { get; }
    object ParametersSchema { get; }
    string? WorkingDirectory { get; set; }
    Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default);
}
