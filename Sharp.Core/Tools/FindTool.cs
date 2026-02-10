using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sharp.AI;

namespace Sharp.Core.Tools;

public sealed class FindTool : IAgentTool
{
    private readonly string _workingDirectory;

    public FindTool(string workingDirectory)
    {
        _workingDirectory = workingDirectory;
        ParametersSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string", description = "Base path to search from (optional)" },
                pattern = new { type = "string", description = "Glob pattern like *.cs, **/*.md" }
            },
            required = new[] { "pattern" }
        }, JsonDefaults.Options);
    }

    public string Name => "find";

    public string Description => "Find files under a path using glob-like wildcard patterns.";

    public JsonElement ParametersSchema { get; }

    public Task<ToolInvocationResult> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        IProgress<ToolInvocationResult>? progress = null,
        CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("pattern", out var patternProp))
            return Task.FromResult(ToolInvocationResult.Text("Missing required argument: pattern", isError: true));

        var pattern = patternProp.GetString();
        if (string.IsNullOrWhiteSpace(pattern))
            return Task.FromResult(ToolInvocationResult.Text("Argument 'pattern' cannot be empty", isError: true));

        var path = arguments.TryGetProperty("path", out var pathProp)
            ? pathProp.GetString()
            : null;

        var workingDirectory = string.IsNullOrWhiteSpace(context.WorkingDirectory)
            ? _workingDirectory
            : context.WorkingDirectory;

        var basePath = string.IsNullOrWhiteSpace(path)
            ? workingDirectory
            : PathResolver.ResolveRead(workingDirectory, path);

        if (!Directory.Exists(basePath))
            return Task.FromResult(ToolInvocationResult.Text($"Directory not found: {basePath}", isError: true));

        var regex = GlobToRegex(pattern);
        var files = new List<string>();

        foreach (var file in Directory.EnumerateFiles(basePath, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(basePath, file).Replace('\\', '/');
            if (!regex.IsMatch(relative))
                continue;

            files.Add(relative);
            if (files.Count >= 2000)
            {
                files.Add("[truncated] listed first 2000 matches");
                break;
            }
        }

        files.Sort(StringComparer.Ordinal);
        return Task.FromResult(ToolInvocationResult.Text(files.Count == 0 ? "No files matched." : string.Join("\n", files)));
    }

    private static Regex GlobToRegex(string pattern)
    {
        var normalized = pattern.Replace('\\', '/');
        var escaped = Regex.Escape(normalized)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", ".");

        return new Regex($"^{escaped}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }
}
