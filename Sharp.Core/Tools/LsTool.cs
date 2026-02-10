using System.Text;
using System.Text.Json;
using Sharp.AI;

namespace Sharp.Core.Tools;

public sealed class LsTool : IAgentTool
{
    private readonly string _workingDirectory;

    public LsTool(string workingDirectory)
    {
        _workingDirectory = workingDirectory;
        ParametersSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string", description = "Directory path to list (optional)" },
                recursive = new { type = "boolean", description = "Recursively list files and directories" },
                includeHidden = new { type = "boolean", description = "Include hidden files" }
            }
        }, JsonDefaults.Options);
    }

    public string Name => "ls";

    public string Description => "List directory contents with optional recursive traversal.";

    public JsonElement ParametersSchema { get; }

    public Task<ToolInvocationResult> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        IProgress<ToolInvocationResult>? progress = null,
        CancellationToken ct = default)
    {
        var path = arguments.TryGetProperty("path", out var pathProp)
            ? pathProp.GetString()
            : null;

        var recursive = arguments.TryGetProperty("recursive", out var recursiveProp)
                        && recursiveProp.ValueKind is JsonValueKind.True;

        var includeHidden = arguments.TryGetProperty("includeHidden", out var hiddenProp)
                            && hiddenProp.ValueKind is JsonValueKind.True;

        var workingDirectory = string.IsNullOrWhiteSpace(context.WorkingDirectory)
            ? _workingDirectory
            : context.WorkingDirectory;

        var target = string.IsNullOrWhiteSpace(path)
            ? workingDirectory
            : PathResolver.ResolveRead(workingDirectory, path);

        if (!Directory.Exists(target) && !File.Exists(target))
            return Task.FromResult(ToolInvocationResult.Text($"Path not found: {target}", isError: true));

        if (File.Exists(target))
            return Task.FromResult(ToolInvocationResult.Text(path ?? target));

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        var lines = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(target, "*", options))
        {
            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileName(entry);
            if (!includeHidden && name.StartsWith(".", StringComparison.Ordinal))
                continue;

            var relative = Path.GetRelativePath(target, entry);
            var prefix = Directory.Exists(entry) ? "[D]" : "[F]";
            lines.Add($"{prefix} {relative}");

            if (lines.Count >= 2000)
            {
                lines.Add("[truncated] listed first 2000 entries");
                break;
            }
        }

        lines.Sort(StringComparer.Ordinal);
        return Task.FromResult(ToolInvocationResult.Text(lines.Count == 0 ? "(empty directory)" : string.Join("\n", lines)));
    }
}
