using Sharp.AI;
using Sharp.Core.Extensions;

namespace Sharp.Core;

public sealed class ToolRuntime
{
    private readonly Dictionary<string, IAgentTool> _toolsByName;
    private readonly ToolExecutionContext _context;
    private readonly ExtensionRuntime? _extensionRuntime;

    public ToolRuntime(
        IEnumerable<IAgentTool> tools,
        ToolExecutionContext? context = null,
        ExtensionRuntime? extensionRuntime = null)
    {
        _toolsByName = new Dictionary<string, IAgentTool>(StringComparer.Ordinal);
        foreach (var tool in tools)
            _toolsByName[tool.Name] = tool;

        _context = context ?? new ToolExecutionContext(Directory.GetCurrentDirectory(), string.Empty);
        _extensionRuntime = extensionRuntime;
    }

    public IReadOnlyList<ToolDefinition> ToToolDefinitions()
        => _toolsByName.Values
            .Select(tool => new ToolDefinition(tool.Name, tool.Description, tool.ParametersSchema))
            .ToList();

    public async Task<ToolInvocationResult> ExecuteAsync(
        ToolCall call,
        IProgress<ToolInvocationResult>? progress = null,
        CancellationToken ct = default)
    {
        if (!_toolsByName.TryGetValue(call.Name, out var tool))
        {
            return ToolInvocationResult.Text($"Tool '{call.Name}' is not registered", isError: true);
        }

        try
        {
            var args = ParseJson(call.ArgumentsJson);
            if (_extensionRuntime != null)
            {
                var decision = await _extensionRuntime.EmitToolCallAsync(
                    new ExtensionToolCallEvent(call.Id, call.Name, args),
                    ct);

                if (decision?.Block == true)
                    return ToolInvocationResult.Text(
                        decision.Reason ?? "Tool execution was blocked by an extension",
                        isError: true);
            }

            var result = await tool.ExecuteAsync(args, _context, progress, ct);
            if (_extensionRuntime == null)
                return result;

            return await _extensionRuntime.EmitToolResultAsync(
                new ExtensionToolResultEvent(call.Id, call.Name, args, result),
                ct);
        }
        catch (Exception ex)
        {
            var args = ParseJson(call.ArgumentsJson);
            var result = ToolInvocationResult.Text($"Tool execution failed: {ex.Message}", isError: true);
            if (_extensionRuntime == null)
                return result;

            return await _extensionRuntime.EmitToolResultAsync(
                new ExtensionToolResultEvent(call.Id, call.Name, args, result),
                ct);
        }
    }

    private static System.Text.Json.JsonElement ParseJson(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json, JsonDefaults.Options);
        }
        catch (System.Text.Json.JsonException)
        {
            return System.Text.Json.JsonSerializer.SerializeToElement(new { });
        }
    }
}
