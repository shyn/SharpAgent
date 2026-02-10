using System.Text.Json;

namespace Sharp.Core.Tools;

public sealed class WriteTool : IAgentTool
{
    private readonly string _workingDirectory;
    private readonly bool _allowOutsideWorkspace;

    public WriteTool(string workingDirectory, bool allowOutsideWorkspace = false)
    {
        _workingDirectory = workingDirectory;
        _allowOutsideWorkspace = allowOutsideWorkspace;

        ParametersSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string", description = "Path to file" },
                content = new { type = "string", description = "File content" }
            },
            required = new[] { "path", "content" }
        }, Sharp.AI.JsonDefaults.Options);
    }

    public string Name => "write";

    public string Description =>
        "Write content to a file. Creates missing parent directories and overwrites existing files.";

    public JsonElement ParametersSchema { get; }

    public async Task<ToolInvocationResult> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        IProgress<ToolInvocationResult>? progress = null,
        CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("path", out var pathProp))
            return ToolInvocationResult.Text("Missing required argument: path", isError: true);

        if (!arguments.TryGetProperty("content", out var contentProp))
            return ToolInvocationResult.Text("Missing required argument: content", isError: true);

        var path = pathProp.GetString();
        var content = contentProp.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(path))
            return ToolInvocationResult.Text("Argument 'path' cannot be empty", isError: true);

        var workingDirectory = string.IsNullOrWhiteSpace(context.WorkingDirectory)
            ? _workingDirectory
            : context.WorkingDirectory;

        string absolutePath;
        try
        {
            absolutePath = PathResolver.ResolveWrite(workingDirectory, path, _allowOutsideWorkspace);
        }
        catch (Exception ex)
        {
            return ToolInvocationResult.Text(ex.Message, isError: true);
        }

        var directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(absolutePath, content, ct);
        return ToolInvocationResult.Text($"Wrote {content.Length} characters to {path}");
    }
}
