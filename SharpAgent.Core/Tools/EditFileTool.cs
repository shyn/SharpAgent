using System.Text.Json;

namespace SharpAgent.Core.Tools;

public sealed class EditFileTool : ITool
{
    public string Name => "edit_file";
    public string Description => """
        Make edits to a text file.

        Replaces 'old_str' with 'new_str' in the given file. 'old_str' and 'new_str' MUST be different from each other.

        If the file specified with path doesn't exist, it will be created.
        """;

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "The file path to edit or create" },
            old_str = new { type = "string", description = "The text to replace (empty string to append to new file)" },
            new_str = new { type = "string", description = "The replacement text" }
        },
        required = new[] { "path", "new_str" }
    };

    public async Task<string> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var (path, oldStr, newStr) = ParseInput(input);

            if (string.IsNullOrEmpty(path))
                return "Error: path is required";

            if (oldStr == newStr)
                return "Error: old_str and new_str must be different";

            if (!File.Exists(path))
            {
                if (string.IsNullOrEmpty(oldStr))
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    await File.WriteAllTextAsync(path, newStr, ct);
                    return "OK";
                }
                return $"Error: File not found: {path}";
            }

            var content = await File.ReadAllTextAsync(path, ct);

            if (!string.IsNullOrEmpty(oldStr) && !content.Contains(oldStr))
                return "Error: old_str not found in file";

            var newContent = content.Replace(oldStr, newStr);
            await File.WriteAllTextAsync(path, newContent, ct);

            return "OK";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static (string path, string oldStr, string newStr) ParseInput(string input)
    {
        using var doc = JsonDocument.Parse(input);
        var root = doc.RootElement;

        var path = root.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
        var oldStr = root.TryGetProperty("old_str", out var o) ? o.GetString() ?? "" : "";
        var newStr = root.TryGetProperty("new_str", out var n) ? n.GetString() ?? "" : "";

        return (path, oldStr, newStr);
    }
}
