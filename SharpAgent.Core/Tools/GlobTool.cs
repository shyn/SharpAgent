using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace SharpAgent.Core.Tools;

public sealed class GlobTool : ITool
{
    public string Name => "glob";
    public string Description => """
Find files and directories using glob patterns. This tool supports standard glob syntax like `*`, `?`, and `**` for recursive searches.
The 'pattern' param is REQUIRED

**When to use:**
- Find files matching specific patterns (e.g., all Python files: `*.py`)
- Search for files recursively in subdirectories (e.g., `src/**/*.js`)
- Locate configuration files (e.g., `*.config.*`, `*.json`)
- Find test files (e.g., `test_*.py`, `*_test.go`)

**Example patterns:**
- `*.py` - All Python files in current directory
- `src/**/*.js` - All JavaScript files in src directory recursively
- `test_*.py` - Python test files starting with "test_"
- `*.config.{js,ts}` - Config files with .js or .ts extension

**Bad example patterns:**
- `**`, `**/*.py` - Any pattern starting with '**' will be rejected. Because it would recursively search all directories and subdirectories, which is very likely to yield large result that exceeds your context size. Always use more specific patterns like `src/**/*.py` instead.
- `node_modules/**/*.js` - Although this does not start with '**', it would still highly possible to yield large result because `node_modules` is well-known to contain too many directories and files. Avoid recursively searching in such directories, other examples include `venv`, `.venv`, `__pycache__`, `target`. If you really need to search in a dependency, use more specific patterns like `node_modules/react/src/*` instead.
""";

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
