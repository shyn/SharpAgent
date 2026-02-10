using System.Text.Json;

namespace Sharp.Core;

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    JsonElement ParametersSchema { get; }

    Task<ToolInvocationResult> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        IProgress<ToolInvocationResult>? progress = null,
        CancellationToken ct = default);
}
