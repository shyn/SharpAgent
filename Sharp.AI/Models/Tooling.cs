using System.Text.Json;

namespace Sharp.AI.Models;

public sealed record ToolDefinition(string Name, string Description, JsonElement ParametersSchema)
{
    public static ToolDefinition FromObject(string name, string description, object schema)
        => new(name, description, JsonSerializer.SerializeToElement(schema, JsonDefaults.Options));
}

public sealed record ToolCall(string Id, string Name, string ArgumentsJson, string? Signature = null);
