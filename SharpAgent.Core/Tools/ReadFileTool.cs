using System.Text.Json;

namespace SharpAgent.Core.Tools;

public sealed class ReadFileTool : ITool
{
    public string Name => "read_file";
    public string? WorkingDirectory { get; set; }
    public string Description => "Read the contents of a given relative file path. Use this when you want to see what's inside a file. Do not use this with directory names.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new { path = new { type = "string", description = "The relative path to the file to read" } },
        required = new[] { "path" }
    };

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var path = ParsePath(input);
            path = ResolvePath(path);

            if (Directory.Exists(path))
                return ToolResult.Error("Path is a directory, not a file.", "INVALID_PATH");

            if (!File.Exists(path))
                return ToolResult.Error($"File not found: {path}", "NOT_FOUND");

            return ToolResult.Success(await File.ReadAllTextAsync(path, ct));
        }
        catch (Exception ex)
        {
            return ToolResult.Error(ex.Message, "EXECUTION_ERROR");
        }
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;
        
        var basePath = WorkingDirectory ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(basePath, path));
    }

    private static string ParsePath(string input)
    {
        input = input.Trim();
        if (!input.StartsWith('{')) return input;

        using var doc = JsonDocument.Parse(input);
        if (doc.RootElement.TryGetProperty("path", out var pathProp))
            return pathProp.GetString() ?? input;
        if (doc.RootElement.TryGetProperty("input", out var inputProp))
            return inputProp.GetString() ?? input;

        return input;
    }
}
