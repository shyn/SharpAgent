using System.Text.Json;
using Sharp.AI;

namespace Sharp.Core;

public sealed record ToolInvocationResult(
    bool IsError,
    IReadOnlyList<ContentBlock> Content,
    JsonElement? Details = null)
{
    public string ContentAsText => string.Join("", Content.Select(block => block switch
    {
        TextContentBlock text => text.Text,
        ToolResultContentBlock result => result.ContentText,
        ThinkingContentBlock thinking => thinking.Text,
        ImageContentBlock image => $"[image:{image.MimeType}]",
        ToolCallContentBlock call => $"[tool_call:{call.ToolName}]",
        _ => string.Empty
    }));

    public static ToolInvocationResult Text(string text, bool isError = false, object? details = null)
    {
        JsonElement? detailsElement = details == null
            ? null
            : JsonSerializer.SerializeToElement(details, JsonDefaults.Options);

        return new ToolInvocationResult(isError, [new TextContentBlock(text)], detailsElement);
    }

    public ToolResultContentBlock ToToolResultBlock(string toolCallId, string toolName)
        => new(toolCallId, toolName, ContentAsText, IsError);
}
