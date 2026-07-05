using System.Text.Json;
using Sharp.AI;
using Sharp.Core.Extensions;

namespace Sharp.Core;

public sealed class ToolRuntime
{
    private readonly Dictionary<string, IAgentTool> _toolsByName;
    private readonly ToolExecutionContext _context;
    private readonly ExtensionRuntime? _extensionRuntime;
    private readonly IReadOnlyList<ToolDefinition> _toolDefinitions;

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

        // Bolt: Cache tool definitions on construction to avoid O(N) list allocations
        // every time ToToolDefinitions is called during the hot AgentLoop.
        _toolDefinitions = _toolsByName.Values
            .Select(tool => new ToolDefinition(tool.Name, tool.Description, tool.ParametersSchema))
            .ToList();
    }

    public IReadOnlyList<ToolDefinition> ToToolDefinitions() => _toolDefinitions;

    public async Task<ToolInvocationResult> ExecuteAsync(
        ToolCall call,
        IProgress<ToolInvocationResult>? progress = null,
        CancellationToken ct = default)
    {
        if (!_toolsByName.TryGetValue(call.Name, out var tool))
        {
            return ToolInvocationResult.Text($"Tool '{call.Name}' is not registered", isError: true);
        }

        if (!TryParseArguments(call.ArgumentsJson, out var args, out var parseError))
        {
            return ToolInvocationResult.Text(
                $"Tool arguments parse failed for '{call.Name}': {parseError}",
                isError: true);
        }

        try
        {
            if (!ToolArgumentsValidator.TryValidate(tool.ParametersSchema, args, out var validationError))
            {
                return ToolInvocationResult.Text(
                    $"Tool arguments validation failed for '{call.Name}': {validationError}",
                    isError: true);
            }

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
            var result = ToolInvocationResult.Text($"Tool execution failed: {ex.Message}", isError: true);
            if (_extensionRuntime == null)
                return result;

            return await _extensionRuntime.EmitToolResultAsync(
                new ExtensionToolResultEvent(call.Id, call.Name, args, result),
                ct);
        }
    }

    private static bool TryParseArguments(string rawJson, out JsonElement arguments, out string error)
    {
        arguments = default;
        error = string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = $"arguments must be a JSON object, got '{doc.RootElement.ValueKind}'.";
                return false;
            }

            arguments = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
