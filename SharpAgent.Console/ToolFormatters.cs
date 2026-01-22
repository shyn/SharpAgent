using System.Text.Json;
using SharpAgent.Core.Streaming;
using Spectre.Console;

namespace SharpAgent.Console;

public interface IToolFormatter
{
    void RenderStart(AgentToolCallStartedEvent toolCall);
    void RenderCompleted(AgentToolCallCompletedEvent toolComplete, string toolName, double elapsedMs);
}

public class ToolFormatterDispatcher
{
    private readonly Dictionary<string, IToolFormatter> _formatters = new();
    private readonly IToolFormatter _defaultFormatter = new DefaultToolFormatter();

    public ToolFormatterDispatcher()
    {
        _formatters["read_file"] = new ReadFileToolFormatter();
        _formatters["edit_file"] = new EditFileToolFormatter();
        _formatters["list_files"] = new ListFilesToolFormatter();
        _formatters["bash"] = new BashToolFormatter();
        _formatters["glob"] = new GlobToolFormatter();
        _formatters["grep"] = new GrepToolFormatter();
    }

    public void RenderStart(AgentToolCallStartedEvent toolCall)
    {
        if (_formatters.TryGetValue(toolCall.ToolName, out var formatter))
        {
            formatter.RenderStart(toolCall);
        }
        else
        {
            _defaultFormatter.RenderStart(toolCall);
        }
    }

    public void RenderCompleted(AgentToolCallCompletedEvent toolComplete, string toolName, double elapsedMs)
    {
        if (_formatters.TryGetValue(toolName, out var formatter))
        {
            formatter.RenderCompleted(toolComplete, toolName, elapsedMs);
        }
        else
        {
            _defaultFormatter.RenderCompleted(toolComplete, toolName, elapsedMs);
        }
    }
}

public abstract class BaseToolFormatter : IToolFormatter
{
    public virtual void RenderStart(AgentToolCallStartedEvent toolCall)
    {
        var argsDisplay = FormatArguments(toolCall.Arguments);
        var truncatedArgs = argsDisplay.Length > 60
            ? argsDisplay[..60] + "..."
            : argsDisplay;

        AnsiConsole.WriteLine();
        AnsiConsole.Write("→ ");
        AnsiConsole.MarkupInterpolated($"{toolCall.ToolName.EscapeMarkup()}({truncatedArgs.EscapeMarkup()})");
        AnsiConsole.WriteLine();
    }

    public abstract void RenderCompleted(AgentToolCallCompletedEvent toolComplete, string toolName, double elapsedMs);

    protected string FormatArguments(string arguments)
    {
        if (!arguments.StartsWith('{')) return arguments;

        try
        {
            using var doc = JsonDocument.Parse(arguments);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var props = new List<string>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var value = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => $"\"{prop.Value.GetString()?.EscapeMarkup() ?? ""}\"",
                        JsonValueKind.Number => prop.Value.ToString(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Array => $"[{prop.Value.GetArrayLength()} items]",
                        JsonValueKind.Object => "{...}",
                        _ => prop.Value.ToString()
                    };
                    props.Add($"{prop.Name}={value}");
                }
                return string.Join(", ", props);
            }
        }
        catch { }
        return arguments;
    }

    protected string GetResultIcon(bool isError) => isError ? "[bold red]✗[/]" : "[bold green]✓[/]";
    protected string GetTimeText(double elapsedMs) => elapsedMs > 0 ? $" [dim]({elapsedMs:F0}ms)[/]" : "";

    protected string TruncateOutput(string output, int maxLines = 10)
    {
        if (string.IsNullOrEmpty(output)) return "";
        var lines = output.Split('\n');
        if (lines.Length <= maxLines) return output.EscapeMarkup();
        var preview = string.Join('\n', lines.Take(maxLines)).EscapeMarkup();
        return $"{preview}\n[dim]... ({lines.Length - maxLines} more lines)[/]";
    }
}

public class DefaultToolFormatter : BaseToolFormatter
{
    public override void RenderCompleted(AgentToolCallCompletedEvent toolComplete, string toolName, double elapsedMs)
    {
        var resultIcon = GetResultIcon(toolComplete.IsError);
        var timeText = GetTimeText(elapsedMs);
        var displayResult = toolComplete.IsError ? $"[red]{toolComplete.Result.EscapeMarkup()}[/]" : TruncateOutput(toolComplete.Result);
        AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: {displayResult}");
    }
}

public class ReadFileToolFormatter : BaseToolFormatter
{
    public override void RenderCompleted(AgentToolCallCompletedEvent toolComplete, string toolName, double elapsedMs)
    {
        var resultIcon = GetResultIcon(toolComplete.IsError);
        var timeText = GetTimeText(elapsedMs);

        if (toolComplete.IsError)
        {
            AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: [red]{toolComplete.Result.EscapeMarkup()}[/]");
            return;
        }

        var lines = toolComplete.Result.Split('\n').Length;
        var bytes = System.Text.Encoding.UTF8.GetByteCount(toolComplete.Result);
        AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: [dim]Read {lines} lines, {bytes} bytes[/]");
    }
}

public class EditFileToolFormatter : BaseToolFormatter
{
    public override void RenderCompleted(AgentToolCallCompletedEvent toolComplete, string toolName, double elapsedMs)
    {
        var resultIcon = GetResultIcon(toolComplete.IsError);
        var timeText = GetTimeText(elapsedMs);

        if (toolComplete.IsError)
        {
            AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: [red]{toolComplete.Result.EscapeMarkup()}[/]");
            return;
        }

        AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: [green]File updated successfully[/]");
        
        // Attempt to show what changed if we can parse the arguments from somewhere or if the tool returned it.
        // For now, since EditFileTool doesn't return the diff, we might just show "Success".
        // BUT, we can try to extract old_str/new_str from the tool call info if we had it.
        // For now, let's just keep it simple or show a placeholder.
    }
}

public class ListFilesToolFormatter : BaseToolFormatter
{
    public override void RenderCompleted(AgentToolCallCompletedEvent toolComplete, string toolName, double elapsedMs)
    {
        var resultIcon = GetResultIcon(toolComplete.IsError);
        var timeText = GetTimeText(elapsedMs);

        if (toolComplete.IsError)
        {
            AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: [red]{toolComplete.Result.EscapeMarkup()}[/]");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(toolComplete.Result);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var dirCount = 0;
                var fileCount = 0;
                var items = new List<string>();
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    var type = item.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "" : "";
                    if (type == "directory") { dirCount++; items.Add($"  [cyan]📁 {name}[/]"); }
                    else { fileCount++; items.Add($"  📄 {name}"); }
                }

                if (items.Count > 0)
                {
                    AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: [dim]{dirCount} dirs, {fileCount} files[/]");
                    foreach (var item in items) AnsiConsole.MarkupLine(item);
                }
                else
                {
                    AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: Empty directory");
                }
                return;
            }
        }
        catch { }
        
        AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: {TruncateOutput(toolComplete.Result)}");
    }
}

public class BashToolFormatter : BaseToolFormatter
{
    public override void RenderCompleted(AgentToolCallCompletedEvent toolComplete, string toolName, double elapsedMs)
    {
        var resultIcon = GetResultIcon(toolComplete.IsError);
        var timeText = GetTimeText(elapsedMs);

        var bashRawLines = toolComplete.Result.Split('\n');
        var exitCodeLine = bashRawLines.FirstOrDefault() ?? "";
        var outputPart = string.Join('\n', bashRawLines.Skip(1));
        
        var exitCode = "0";
        if (exitCodeLine.StartsWith("Exit code:")) exitCode = exitCodeLine.Replace("Exit code:", "").Trim();
        
        var displayResult = string.IsNullOrWhiteSpace(outputPart) 
            ? $"Exit code: {exitCode}" 
            : $"Exit code: {exitCode}\n{TruncateOutput(outputPart)}";

        AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: {displayResult}");
    }
}

public class GlobToolFormatter : BaseToolFormatter
{
    public override void RenderCompleted(AgentToolCallCompletedEvent toolComplete, string toolName, double elapsedMs)
    {
        var resultIcon = GetResultIcon(toolComplete.IsError);
        var timeText = GetTimeText(elapsedMs);

        if (toolComplete.IsError)
        {
            AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: [red]{toolComplete.Result.EscapeMarkup()}[/]");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(toolComplete.Result);
            if (doc.RootElement.TryGetProperty("matchCount", out var countProp))
            {
                AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: Found {countProp.GetInt32()} files");
                return;
            }
        }
        catch { }
        AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: {TruncateOutput(toolComplete.Result)}");
    }
}

public class GrepToolFormatter : BaseToolFormatter
{
    public override void RenderCompleted(AgentToolCallCompletedEvent toolComplete, string toolName, double elapsedMs)
    {
        var resultIcon = GetResultIcon(toolComplete.IsError);
        var timeText = GetTimeText(elapsedMs);

        if (toolComplete.IsError)
        {
            AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: [red]{toolComplete.Result.EscapeMarkup()}[/]");
            return;
        }

        var grepLines = toolComplete.Result.Split('\n');
        var grepFileCount = grepLines.Where(line => line.Contains(':') && !line.StartsWith("No matches")).Select(line => line.Split(':')[0]).Distinct().Count();
        var matchCount = grepLines.Count(line => line.Contains(':') && !line.StartsWith("No matches"));

        string displayResult = (grepFileCount > 0) 
            ? $"Found {matchCount} matches in {grepFileCount} files" 
            : (toolComplete.Result.StartsWith("No matches") ? "No matches found" : TruncateOutput(toolComplete.Result));

        AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: {displayResult}");
    }
}
