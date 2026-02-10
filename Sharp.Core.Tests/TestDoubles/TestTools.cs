using System.Text.Json;
using Sharp.AI;
using Sharp.Core;

namespace Sharp.Core.Tests.TestDoubles;

public sealed class EchoTool : IAgentTool
{
    public string Name => "echo";

    public string Description => "Echoes value";

    public JsonElement ParametersSchema => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            value = new { type = "string" }
        },
        required = new[] { "value" }
    }, JsonDefaults.Options);

    public Task<ToolInvocationResult> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        IProgress<ToolInvocationResult>? progress = null,
        CancellationToken ct = default)
    {
        var value = arguments.TryGetProperty("value", out var prop)
            ? prop.GetString() ?? string.Empty
            : string.Empty;
        return Task.FromResult(ToolInvocationResult.Text($"echo:{value}"));
    }
}

public sealed class PartialUpdateTool : IAgentTool
{
    public string Name => "partial_tool";

    public string Description => "Emits partial updates before completion";

    public JsonElement ParametersSchema => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            value = new { type = "string" }
        },
        required = new[] { "value" }
    }, JsonDefaults.Options);

    public async Task<ToolInvocationResult> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        IProgress<ToolInvocationResult>? progress = null,
        CancellationToken ct = default)
    {
        var value = arguments.TryGetProperty("value", out var prop)
            ? prop.GetString() ?? string.Empty
            : string.Empty;

        progress?.Report(ToolInvocationResult.Text($"partial-1:{value}"));
        await Task.Delay(10, ct);
        progress?.Report(ToolInvocationResult.Text($"partial-2:{value}"));
        await Task.Delay(10, ct);

        return ToolInvocationResult.Text($"final:{value}");
    }
}
