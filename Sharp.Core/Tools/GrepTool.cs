using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sharp.AI;

namespace Sharp.Core.Tools;

public sealed class GrepTool : IAgentTool
{
    private readonly string _workingDirectory;

    public GrepTool(string workingDirectory)
    {
        _workingDirectory = workingDirectory;
        ParametersSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                pattern = new { type = "string", description = "Regex pattern" },
                path = new { type = "string", description = "File or directory to search (optional)" },
                ignoreCase = new { type = "boolean", description = "Ignore case" }
            },
            required = new[] { "pattern" }
        }, JsonDefaults.Options);
    }

    public string Name => "grep";

    public string Description => "Search file contents with regular expressions.";

    public JsonElement ParametersSchema { get; }

    public async Task<ToolInvocationResult> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        IProgress<ToolInvocationResult>? progress = null,
        CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("pattern", out var patternProp))
            return ToolInvocationResult.Text("Missing required argument: pattern", isError: true);

        var pattern = patternProp.GetString();
        if (string.IsNullOrWhiteSpace(pattern))
            return ToolInvocationResult.Text("Argument 'pattern' cannot be empty", isError: true);

        var path = arguments.TryGetProperty("path", out var pathProp)
            ? pathProp.GetString()
            : null;

        var ignoreCase = arguments.TryGetProperty("ignoreCase", out var ignoreCaseProp)
                         && ignoreCaseProp.ValueKind is JsonValueKind.True;

        var workingDirectory = string.IsNullOrWhiteSpace(context.WorkingDirectory)
            ? _workingDirectory
            : context.WorkingDirectory;

        var target = string.IsNullOrWhiteSpace(path)
            ? workingDirectory
            : PathResolver.ResolveRead(workingDirectory, path);

        if (!Directory.Exists(target) && !File.Exists(target))
            return ToolInvocationResult.Text($"Path not found: {target}", isError: true);

        var files = File.Exists(target)
            ? new[] { target }
            : Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories);

        var options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        if (ignoreCase)
            options |= RegexOptions.IgnoreCase;

        Regex regex;
        try
        {
            regex = new Regex(pattern, options);
        }
        catch (Exception ex)
        {
            return ToolInvocationResult.Text($"Invalid regex pattern: {ex.Message}", isError: true);
        }

        var matches = new List<string>();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            byte[] buffer;
            try
            {
                buffer = await File.ReadAllBytesAsync(file, ct);
            }
            catch
            {
                continue;
            }

            if (LooksBinary(buffer))
                continue;

            var text = Encoding.UTF8.GetString(buffer);
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!regex.IsMatch(lines[i]))
                    continue;

                var relative = Path.GetRelativePath(workingDirectory, file).Replace('\\', '/');
                matches.Add($"{relative}:{i + 1}:{lines[i]}");

                if (matches.Count >= 2000)
                {
                    matches.Add("[truncated] listed first 2000 matches");
                    return ToolInvocationResult.Text(string.Join("\n", matches));
                }
            }
        }

        return ToolInvocationResult.Text(matches.Count == 0 ? "No matches found." : string.Join("\n", matches));
    }

    private static bool LooksBinary(byte[] bytes)
    {
        var sampleLength = Math.Min(bytes.Length, 1024);
        for (var i = 0; i < sampleLength; i++)
        {
            if (bytes[i] == 0)
                return true;
        }

        return false;
    }
}
