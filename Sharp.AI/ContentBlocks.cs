using System.Text.Json.Serialization;

namespace Sharp.AI;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextContentBlock), "text")]
[JsonDerivedType(typeof(ImageContentBlock), "image")]
[JsonDerivedType(typeof(ThinkingContentBlock), "thinking")]
[JsonDerivedType(typeof(ToolCallContentBlock), "tool_call")]
[JsonDerivedType(typeof(ToolResultContentBlock), "tool_result")]
public abstract record ContentBlock;

public sealed record TextContentBlock(string Text) : ContentBlock;

public sealed record ImageContentBlock(string MimeType, string Base64Data) : ContentBlock;

public sealed record ThinkingContentBlock(string Text, string? Signature = null) : ContentBlock;

public sealed record ToolCallContentBlock(string ToolCallId, string ToolName, string ArgumentsJson, string? Signature = null) : ContentBlock;

public sealed record ToolResultContentBlock(string ToolCallId, string ToolName, string ContentText, bool IsError) : ContentBlock;
