using System.Text.Json;

namespace SharpAgent.Core.Tools;

public sealed class ReadFileTool : ITool
{
    public string Name => "read_file";
    public string Description => "Read the contents of a given relative file path. Use this when you want to see what's inside a file. Do not use this with directory names.";

    public async Task<string> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var path = ParsePath(input);

            if (Directory.Exists(path))
                return "Error: Path is a directory, not a file.";

            if (!File.Exists(path))
                return $"Error: File not found: {path}";

            return await File.ReadAllTextAsync(path, ct);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static string ParsePath(string input)
    {
        input = input.Trim();
        if (!input.StartsWith('{')) return input;

        using var doc = JsonDocument.Parse(input);
        if (doc.RootElement.TryGetProperty("path", out var prop))
            return prop.GetString() ?? input;

        return input;
    }
}
