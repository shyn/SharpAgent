using System.Text.Json.Serialization;

namespace SharpAgent.Core.Sessions;

public sealed record TimestampedMessage
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }
    
    [JsonPropertyName("role")]
    public string Role { get; init; } = "";
    
    [JsonPropertyName("content")]
    public string Content { get; init; } = "";
    
    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }
    
    [JsonPropertyName("toolCallId")]
    public string? ToolCallId { get; init; }
    
    [JsonPropertyName("toolCalls")]
    public IReadOnlyList<ToolCallData>? ToolCalls { get; init; }
    
    [JsonPropertyName("thinking")]
    public string? Thinking { get; init; }
}

public sealed record ToolCallData
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    
    [JsonPropertyName("arguments")]
    public string Arguments { get; init; } = "";
}
