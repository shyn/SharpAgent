namespace SharpAgent.AI.Types;

/// <summary>
/// Represents text content in a message.
/// </summary>
public class TextContent
{
    /// <summary>
    /// The type of content. Always "text".
    /// </summary>
    public string Type { get; } = "text";

    /// <summary>
    /// The text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Optional signature for the text content (e.g., for OpenAI responses, the message ID).
    /// </summary>
    public string? TextSignature { get; set; }
}

/// <summary>
/// Represents thinking/reasoning content in a message.
/// </summary>
public class ThinkingContent
{
    /// <summary>
    /// The type of content. Always "thinking".
    /// </summary>
    public string Type { get; } = "thinking";

    /// <summary>
    /// The thinking/reasoning content.
    /// </summary>
    public string Thinking { get; set; } = string.Empty;

    /// <summary>
    /// Optional signature for the thinking content (e.g., for OpenAI responses, the reasoning item ID).
    /// </summary>
    public string? ThinkingSignature { get; set; }
}

/// <summary>
/// Represents image content in a message.
/// </summary>
public class ImageContent
{
    /// <summary>
    /// The type of content. Always "image".
    /// </summary>
    public string Type { get; } = "image";

    /// <summary>
    /// Base64 encoded image data.
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// MIME type of the image (e.g., "image/jpeg", "image/png").
    /// </summary>
    public string MimeType { get; set; } = string.Empty;
}

/// <summary>
/// Represents a tool call made by the model.
/// </summary>
public class ToolCall
{
    /// <summary>
    /// The type of content. Always "toolCall".
    /// </summary>
    public string Type { get; } = "toolCall";

    /// <summary>
    /// Unique identifier for the tool call.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Name of the tool being called.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Arguments passed to the tool as a dictionary.
    /// </summary>
    public Dictionary<string, object?> Arguments { get; set; } = new();

    /// <summary>
    /// Optional Google-specific opaque signature for reusing thought context.
    /// </summary>
    public string? ThoughtSignature { get; set; }
}

/// <summary>
/// Represents token usage and cost information.
/// </summary>
public class Usage
{
    /// <summary>
    /// Number of tokens used for input.
    /// </summary>
    public long Input { get; set; }

    /// <summary>
    /// Number of tokens used for output.
    /// </summary>
    public long Output { get; set; }

    /// <summary>
    /// Number of tokens read from cache.
    /// </summary>
    public long CacheRead { get; set; }

    /// <summary>
    /// Number of tokens written to cache.
    /// </summary>
    public long CacheWrite { get; set; }

    /// <summary>
    /// Total number of tokens used.
    /// </summary>
    public long TotalTokens { get; set; }

    /// <summary>
    /// Cost breakdown for the request.
    /// </summary>
    public CostBreakdown Cost { get; set; } = new();
}

/// <summary>
/// Represents a detailed breakdown of costs.
/// </summary>
public class CostBreakdown
{
    /// <summary>
    /// Cost for input tokens.
    /// </summary>
    public decimal Input { get; set; }

    /// <summary>
    /// Cost for output tokens.
    /// </summary>
    public decimal Output { get; set; }

    /// <summary>
    /// Cost for cache read operations.
    /// </summary>
    public decimal CacheRead { get; set; }

    /// <summary>
    /// Cost for cache write operations.
    /// </summary>
    public decimal CacheWrite { get; set; }

    /// <summary>
    /// Total cost.
    /// </summary>
    public decimal Total { get; set; }
}
