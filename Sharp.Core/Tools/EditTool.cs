using System.Text;
using System.Text.Json;
using Sharp.AI;

namespace Sharp.Core.Tools;

public sealed class EditTool : IAgentTool
{
    private readonly string _workingDirectory;
    private readonly bool _allowOutsideWorkspace;

    public EditTool(string workingDirectory, bool allowOutsideWorkspace = false)
    {
        _workingDirectory = workingDirectory;
        _allowOutsideWorkspace = allowOutsideWorkspace;

        ParametersSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string", description = "Path to file" },
                oldText = new { type = "string", description = "Exact old text to replace" },
                newText = new { type = "string", description = "Replacement text" }
            },
            required = new[] { "path", "oldText", "newText" }
        }, JsonDefaults.Options);
    }

    public string Name => "edit";

    public string Description =>
        "Replace exactly one matching text fragment in a file and return a minimal diff summary.";

    public JsonElement ParametersSchema { get; }

    public async Task<ToolInvocationResult> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        IProgress<ToolInvocationResult>? progress = null,
        CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("path", out var pathProp)
            || !arguments.TryGetProperty("oldText", out var oldTextProp)
            || !arguments.TryGetProperty("newText", out var newTextProp))
        {
            return ToolInvocationResult.Text("Missing required arguments: path, oldText, newText", isError: true);
        }

        var path = pathProp.GetString();
        var oldText = oldTextProp.GetString() ?? string.Empty;
        var newText = newTextProp.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(path))
            return ToolInvocationResult.Text("Argument 'path' cannot be empty", isError: true);

        if (oldText == newText)
            return ToolInvocationResult.Text("oldText and newText must be different", isError: true);

        var workingDirectory = string.IsNullOrWhiteSpace(context.WorkingDirectory)
            ? _workingDirectory
            : context.WorkingDirectory;

        string absolutePath;
        try
        {
            absolutePath = PathResolver.ResolveWrite(workingDirectory, path, _allowOutsideWorkspace);
        }
        catch (Exception ex)
        {
            return ToolInvocationResult.Text(ex.Message, isError: true);
        }

        if (!File.Exists(absolutePath))
            return ToolInvocationResult.Text($"File not found: {absolutePath}", isError: true);

        var rawContent = await File.ReadAllTextAsync(absolutePath, ct);
        var hasBom = rawContent.Length > 0 && rawContent[0] == '\uFEFF';
        var contentWithoutBom = hasBom ? rawContent[1..] : rawContent;

        var lineEnding = DetectLineEnding(contentWithoutBom);
        var normalizedContent = NormalizeLineEndings(contentWithoutBom);
        var normalizedOld = NormalizeLineEndings(oldText);
        var normalizedNew = NormalizeLineEndings(newText);

        var firstIndex = normalizedContent.IndexOf(normalizedOld, StringComparison.Ordinal);
        if (firstIndex < 0)
            return ToolInvocationResult.Text("oldText not found in file", isError: true);

        var secondIndex = normalizedContent.IndexOf(normalizedOld, firstIndex + normalizedOld.Length, StringComparison.Ordinal);
        if (secondIndex >= 0)
            return ToolInvocationResult.Text("oldText is not unique in file, please provide more context", isError: true);

        var replacedNormalized = normalizedContent.Replace(normalizedOld, normalizedNew, StringComparison.Ordinal);
        var restoredContent = RestoreLineEndings(replacedNormalized, lineEnding);
        if (hasBom)
            restoredContent = "\uFEFF" + restoredContent;

        await File.WriteAllTextAsync(absolutePath, restoredContent, ct);

        var firstChangedLine = 1;
        for (var i = 0; i < firstIndex; i++)
        {
            if (normalizedContent[i] == '\n')
                firstChangedLine++;
        }

        var diff = BuildUnifiedDiff(path, normalizedOld, normalizedNew);
        var details = JsonSerializer.SerializeToElement(new
        {
            firstChangedLine,
            diff
        }, JsonDefaults.Options);

        return new ToolInvocationResult(
            IsError: false,
            Content: [new TextContentBlock($"Updated {path} at line {firstChangedLine}.")],
            Details: details);
    }

    private static string NormalizeLineEndings(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string DetectLineEnding(string text)
        => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string RestoreLineEndings(string text, string lineEnding)
        => lineEnding == "\r\n"
            ? text.Replace("\n", "\r\n", StringComparison.Ordinal)
            : text;

    private static string BuildUnifiedDiff(string path, string oldText, string newText)
    {
        var oldLines = oldText.Split('\n');
        var newLines = newText.Split('\n');

        var builder = new StringBuilder();
        builder.AppendLine($"--- a/{path}");
        builder.AppendLine($"+++ b/{path}");
        builder.AppendLine("@@");

        foreach (var line in oldLines)
            builder.AppendLine($"-{line}");

        foreach (var line in newLines)
            builder.AppendLine($"+{line}");

        return builder.ToString().TrimEnd();
    }
}
