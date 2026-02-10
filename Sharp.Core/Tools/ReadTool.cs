using System.Text;
using System.Text.Json;
using Sharp.AI;

namespace Sharp.Core.Tools;

public sealed class ReadTool : IAgentTool
{
    private const int DefaultMaxLines = 500;
    private const int DefaultMaxBytes = 100 * 1024;

    private readonly string _workingDirectory;

    public ReadTool(string workingDirectory)
    {
        _workingDirectory = workingDirectory;
        ParametersSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string", description = "Path to the file" },
                offset = new { type = "integer", description = "Line offset, 1-indexed (optional)" },
                limit = new { type = "integer", description = "Maximum lines to read (optional)" }
            },
            required = new[] { "path" }
        }, JsonDefaults.Options);
    }

    public string Name => "read";

    public string Description =>
        "Read text or image files. Supports optional offset/limit for large files and truncates overly large outputs.";

    public JsonElement ParametersSchema { get; }

    public async Task<ToolInvocationResult> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        IProgress<ToolInvocationResult>? progress = null,
        CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("path", out var pathProp))
            return ToolInvocationResult.Text("Missing required argument: path", isError: true);

        var path = pathProp.GetString();
        if (string.IsNullOrWhiteSpace(path))
            return ToolInvocationResult.Text("Argument 'path' cannot be empty", isError: true);

        var workingDirectory = string.IsNullOrWhiteSpace(context.WorkingDirectory)
            ? _workingDirectory
            : context.WorkingDirectory;

        var absolutePath = PathResolver.ResolveRead(workingDirectory, path);
        if (!File.Exists(absolutePath))
            return ToolInvocationResult.Text($"File not found: {absolutePath}", isError: true);

        var bytes = await File.ReadAllBytesAsync(absolutePath, ct);

        if (LooksBinary(bytes))
        {
            var ext = Path.GetExtension(absolutePath).ToLowerInvariant();
            if (IsImageExtension(ext))
            {
                var mime = ext switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    _ => "application/octet-stream"
                };

                var base64 = Convert.ToBase64String(bytes);
                return new ToolInvocationResult(
                    IsError: false,
                    Content:
                    [
                        new TextContentBlock($"Read image file: {path} ({bytes.Length} bytes)"),
                        new ImageContentBlock(mime, base64)
                    ]);
            }

            return ToolInvocationResult.Text($"Binary file detected: {path} ({bytes.Length} bytes)");
        }

        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split('\n');

        var offset = arguments.TryGetProperty("offset", out var offsetProp) && offsetProp.TryGetInt32(out var offsetValue)
            ? Math.Max(1, offsetValue)
            : 1;

        var startIndex = offset - 1;
        if (startIndex >= lines.Length)
            return ToolInvocationResult.Text($"Offset {offset} is out of range (file has {lines.Length} lines)", isError: true);

        var selected = lines.AsEnumerable().Skip(startIndex);
        if (arguments.TryGetProperty("limit", out var limitProp) && limitProp.TryGetInt32(out var limitValue) && limitValue > 0)
            selected = selected.Take(limitValue);

        var candidates = selected.ToList();
        var window = new List<string>(Math.Min(DefaultMaxLines, candidates.Count));

        var truncatedByLines = false;
        var truncatedByBytes = false;
        var usedBytes = 0;

        foreach (var line in candidates)
        {
            if (window.Count >= DefaultMaxLines)
            {
                truncatedByLines = true;
                break;
            }

            var segment = window.Count == 0 ? line : "\n" + line;
            var segmentBytes = Encoding.UTF8.GetByteCount(segment);

            if (usedBytes + segmentBytes > DefaultMaxBytes)
            {
                if (window.Count == 0)
                {
                    var takeChars = Math.Min(line.Length, DefaultMaxBytes);
                    window.Add(line[..takeChars]);
                }

                truncatedByBytes = true;
                break;
            }

            window.Add(line);
            usedBytes += segmentBytes;
        }

        var content = string.Join("\n", window);

        if (truncatedByLines || truncatedByBytes)
        {
            var nextOffset = startIndex + window.Count + 1;
            var reason = truncatedByLines
                ? $"Truncated to {DefaultMaxLines} lines"
                : $"Truncated to {DefaultMaxBytes / 1024}KB";

            content += $"\n\n[{reason}. Use offset={nextOffset} to continue.]";
        }

        return ToolInvocationResult.Text(content);
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

    private static bool IsImageExtension(string ext)
        => ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp";
}
