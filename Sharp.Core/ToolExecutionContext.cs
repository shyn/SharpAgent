namespace Sharp.Core;

public sealed record ToolExecutionContext(
    string WorkingDirectory,
    string SessionId);
