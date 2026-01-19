using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace SharpAgent.Core.Tools;

public sealed class GlobTool : ITool
{
    public string Name => "glob";
    public string Description => "Find files matching a glob pattern. Supports patterns like '**/*.cs', 'src/**/*.txt', '*.json'. Works on both Windows and Unix systems.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            pattern = new { type = "string", description = "The glob pattern to match files (e.g., '**/*.cs', 'src/**/*.txt')" },
            path = new { type = "string", description = "The base directory to search in (optional, defaults to current directory)" }
        },
        required = new[] { "pattern" }
    };

    public Task<string> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var (pattern, basePath) = ParseInput(input);

            if (string.IsNullOrWhiteSpace(pattern))
                return Task.FromResult("Error: Pattern is required");

            var searchDir = string.IsNullOrEmpty(basePath) ? Directory.GetCurrentDirectory() : basePath;

            if (!Path.IsPathRooted(searchDir))
                searchDir = Path.GetFullPath(searchDir);

            if (!Directory.Exists(searchDir))
                return Task.FromResult($"Error: Directory not found: {searchDir}");

            var matcher = new Matcher();
            matcher.AddInclude(pattern);

            var directoryInfo = new DirectoryInfoWrapper(new DirectoryInfo(searchDir));
            var result = matcher.Execute(directoryInfo);

            var files = result.Files
                .Select(f => f.Path.Replace('\\', '/'))
                .OrderBy(f => f)
                .ToList();

            return Task.FromResult(JsonSerializer.Serialize(new
            {
                basePath = searchDir,
                pattern,
                matchCount = files.Count,
                files
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    private static (string pattern, string path) ParseInput(string input)
    {
        input = input.Trim();

        if (!input.StartsWith('{'))
            return (input, "");

        using var doc = JsonDocument.Parse(input);
        var root = doc.RootElement;

        var pattern = root.TryGetProperty("pattern", out var patternProp)
            ? patternProp.GetString() ?? ""
            : "";

        var path = root.TryGetProperty("path", out var pathProp)
            ? pathProp.GetString() ?? ""
            : "";

        return (pattern, path);
    }
}
