namespace SharpAgent.AI.Types;

/// <summary>
/// Enumeration of possible reasons why a model stopped generating content.
/// </summary>
public enum StopReason
{
    /// <summary>
    /// Model reached a natural stop point (e.g., end of response).
    /// </summary>
    Stop,

    /// <summary>
    /// Model reached the maximum token limit.
    /// </summary>
    Length,

    /// <summary>
    /// Model called a tool/function.
    /// </summary>
    ToolUse,

    /// <summary>
    /// An error occurred during generation.
    /// </summary>
    Error,

    /// <summary>
    /// The request was aborted.
    /// </summary>
    Aborted
}

/// <summary>
/// Represents a message from the user.
/// </summary>
public class UserMessage : IMessage
{
    /// <summary>
    /// The role of the message sender. Always "user".
    /// </summary>
    public string Role { get; } = "user";

    /// <summary>
    /// The content of the message, either a plain string or a collection of structured content blocks.
    /// </summary>
    public required object Content { get; set; }

    /// <summary>
    /// Unix timestamp in milliseconds when the message was created.
    /// </summary>
    public required long Timestamp { get; set; }
}

/// <summary>
/// Represents a message from the assistant/model.
/// </summary>
public class AssistantMessage : IMessage
{
    /// <summary>
    /// The role of the message sender. Always "assistant".
    /// </summary>
    public string Role { get; } = "assistant";

    /// <summary>
    /// The content of the message, containing text, thinking, and/or tool calls.
    /// a list of textcontent, thinkingcontent or toolcalls
    /// TODO: proper typing instead of object
    /// </summary>
    public required List<object> Content { get; set; }

    /// <summary>
    /// The API provider that generated this message.
    /// </summary>
    public required string Api { get; set; }

    /// <summary>
    /// The specific provider (e.g., OpenAI, Anthropic, Google).
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// The model identifier used to generate this message.
    /// </summary>
    public required string Model { get; set; }

    /// <summary>
    /// Token usage and cost information for this message.
    /// </summary>
    public required Usage Usage { get; set; }

    /// <summary>
    /// The reason why the model stopped generating content.
    /// </summary>
    public required StopReason StopReason { get; set; }

    /// <summary>
    /// Optional error message if an error occurred during generation.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Unix timestamp in milliseconds when the message was created.
    /// </summary>
    public required long Timestamp { get; set; }
}

/// <summary>
/// Represents a message containing the result of a tool/function call.
/// </summary>
public class ToolResultMessage : IMessage
{
    /// <summary>
    /// The role of the message sender. Always "toolResult".
    /// </summary>
    public string Role { get; } = "toolResult";

    /// <summary>
    /// The unique identifier of the tool call this result corresponds to.
    /// </summary>
    public required string ToolCallId { get; set; }

    /// <summary>
    /// The name of the tool that was executed.
    /// </summary>
    public required string ToolName { get; set; }

    /// <summary>
    /// The content of the tool result, supporting text and images.
    /// </summary>
    public required List<object> Content { get; set; }

    /// <summary>
    /// Optional details object containing additional information about the tool result.
    /// </summary>
    public object? Details { get; set; }

    /// <summary>
    /// Indicates whether the tool execution resulted in an error.
    /// </summary>
    public required bool IsError { get; set; }

    /// <summary>
    /// Unix timestamp in milliseconds when the message was created.
    /// </summary>
    public required long Timestamp { get; set; }
}

public class ToolResultMessage : IMessage
{
	public string Role => "toolResult";
    public string ToolCallId {get;set;}
    public string ToolName {get;set;}
    public List<object> Content {get;set;}
    public object? Details {get;set;}
	public bool IsError {get;set;}
	public required long Timestamp { get; set; }
}

/// <summary>
/// Union type representing any message in a conversation.
/// Can be a UserMessage, AssistantMessage, or ToolResultMessage.
/// </summary>
public interface IMessage
{
    /// <summary>
    /// The role of the message sender.
    /// </summary>
    public string Role { get; }

    /// <summary>
    /// Unix timestamp in milliseconds when the message was created.
    /// </summary>
    public long Timestamp { get; set; }
}
