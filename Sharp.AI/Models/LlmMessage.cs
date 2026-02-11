namespace Sharp.AI.Models;

public sealed record LlmMessage(
    LlmMessageRole Role,
    IReadOnlyList<ContentBlock> Content,
    LlmStopReason? StopReason = null,
    string? ErrorMessage = null)
{
    public static LlmMessage SystemText(string text) => new(LlmMessageRole.System, [new TextContentBlock(text)]);

    public static LlmMessage UserText(string text) => new(LlmMessageRole.User, [new TextContentBlock(text)]);

    public static LlmMessage AssistantText(string text) => new(LlmMessageRole.Assistant, [new TextContentBlock(text)]);

    public static LlmMessage ToolResult(string toolCallId, string toolName, string content, bool isError = false)
        => new(LlmMessageRole.Tool, [new ToolResultContentBlock(toolCallId, toolName, content, isError)]);
}
