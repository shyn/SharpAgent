using System.Text.Json.Serialization;

namespace SharpAgent.Api.Models;

public sealed record ChatRequest
{
    public required string Message { get; init; }
    public string ThinkingLevel { get; init; } = "off";
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? ApiKey { get; init; }
}

public sealed record ChatEventDto
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }
    
    [JsonPropertyName("data")]
    public object? Data { get; init; }
}

public sealed record TextDeltaData
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

public sealed record ThinkingDeltaData
{
    [JsonPropertyName("thinking")]
    public required string Thinking { get; init; }
}

public sealed record ThinkingCompletedData
{
    [JsonPropertyName("fullThinking")]
    public required string FullThinking { get; init; }
}

public sealed record ToolUseStartedData
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

public sealed record ToolUseArgumentsDeltaData
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    
    [JsonPropertyName("partialJson")]
    public required string PartialJson { get; init; }
}

public sealed record ToolUseCompletedData
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
}

public sealed record ToolCallStartedData
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    
    [JsonPropertyName("name")]
    public required string Name { get; init; }
    
    [JsonPropertyName("arguments")]
    public required string Arguments { get; init; }
}

public sealed record ToolCallCompletedData
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    
    [JsonPropertyName("result")]
    public required string Result { get; init; }
    
    [JsonPropertyName("isError")]
    public bool IsError { get; init; }
}

public sealed record CompletedData
{
    [JsonPropertyName("finalAnswer")]
    public required string FinalAnswer { get; init; }
}

public sealed record ErrorData
{
    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

public sealed record ConfigResponse
{
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }
    
    [JsonPropertyName("model")]
    public required string Model { get; init; }
    
    [JsonPropertyName("hasApiKey")]
    public bool HasApiKey { get; init; }
}
