using System.Text.Json;

namespace SharpAgent.Core.Tools;

public sealed class ListFilesTool : ITool
{
    public string Name => "list_files";
    public string Description => "List files and directories at a given path. If no path is provided, lists files in the current directory.";

    public Task<string> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var path = ParsePath(input);
            var dir = string.IsNullOrEmpty(path) ? "." : path;

            if (!Directory.Exists(dir))
                return Task.FromResult($"Error: Directory not found: {dir}");

            var files = new List<string>();

            foreach (var entry in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(dir, entry);
                if (Directory.Exists(entry))
                    files.Add(relPath.Replace('\\', '/') + "/");
                else
                    files.Add(relPath.Replace('\\', '/'));
            }

            return Task.FromResult(JsonSerializer.Serialize(files));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    private static string ParsePath(string input)
    {
        input = input.Trim();
        if (!input.StartsWith('{')) return input;

        using var doc = JsonDocument.Parse(input);
        if (doc.RootElement.TryGetProperty("path", out var prop))
            return prop.GetString() ?? "";

        return "";
    }
}
