using System.Text.Json;

namespace SharpAgent.Core.Tools;

public sealed class ListFilesTool : ITool
{
    public string Name => "list_files";
    public string? WorkingDirectory { get; set; }
    public string Description => "List files and directories at a given path (non-recursive). Returns name and type (extension or 'directory') for each entry. Directories have a trailing '/'. If no path is provided, lists files in the current directory.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new { path = new { type = "string", description = "The directory path to list (optional, defaults to current directory)" } }
    };

    public Task<string> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var path = ParsePath(input);
            var dir = string.IsNullOrEmpty(path) ? "." : path;
            dir = ResolvePath(dir);

            if (!Directory.Exists(dir))
                return Task.FromResult($"Error: Directory not found: {dir}");

            var entries = Directory.GetFileSystemEntries(dir);
            var fileInfo = entries.Select(entry =>
            {
                var name = Path.GetFileName(entry);
                var isDirectory = Directory.Exists(entry);
                var fileType = isDirectory ? "directory" : Path.GetExtension(entry).TrimStart('.').ToLowerInvariant();

                if (string.IsNullOrEmpty(fileType) && !isDirectory)
                    fileType = "file";

                return new
                {
                    name = isDirectory ? $"{name}/" : name,
                    type = fileType
                };
            }).OrderBy(x => x.type == "directory" ? 0 : 1).ThenBy(x => x.name.TrimEnd('/'));

            return Task.FromResult(JsonSerializer.Serialize(fileInfo));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
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
            return pathProp.GetString() ?? "";
        if (doc.RootElement.TryGetProperty("input", out var inputProp))
            return inputProp.GetString() ?? "";

        return "";
    }
}
