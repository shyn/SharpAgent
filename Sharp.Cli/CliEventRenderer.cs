using System.Text;
using System.Text.Json;
using Sharp.AI;
using Sharp.Core;

namespace Sharp.Cli;

internal sealed class CliEventRenderer
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly TextWriter _outputWriter;
    private readonly TextWriter _errorWriter;
    private readonly Dictionary<string, string> _toolNamesByCallId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StringBuilder> _toolArgumentsByCallId = new(StringComparer.Ordinal);
    private readonly StringBuilder _thinkingBuffer = new();
    private bool _hasOpenTextLine;
    private bool _hasTextDeltaInCurrentPrompt;

    public CliEventRenderer(TextWriter? outputWriter = null, TextWriter? errorWriter = null)
    {
        _outputWriter = outputWriter ?? Console.Out;
        _errorWriter = errorWriter ?? Console.Error;
    }

    public void Render(AgentEvent evt)
    {
        switch (evt)
        {
            case AgentStartedEvent started:
                EndTextLine();
                _hasTextDeltaInCurrentPrompt = false;
                RenderAgentStarted(started);
                break;
            case AgentThinkingStartedEvent:
                EndTextLine();
                _thinkingBuffer.Clear();
                _errorWriter.WriteLine(Ansi.Color("[thinking:start]", Ansi.Gray));
                break;
            case AgentThinkingDeltaEvent thinkingDelta:
                _thinkingBuffer.Append(thinkingDelta.Delta);
                break;
            case AgentThinkingCompletedEvent thinkingCompleted:
                EndTextLine();
                RenderThinkingCompleted(thinkingCompleted);
                break;
            case AgentTextDeltaEvent textDelta:
                _outputWriter.Write(textDelta.Delta);
                _outputWriter.Flush();
                _hasOpenTextLine = true;
                _hasTextDeltaInCurrentPrompt = true;
                break;
            case AgentToolUseStartedEvent toolUseStarted:
                EndTextLine();
                _toolNamesByCallId[toolUseStarted.ToolCallId] = toolUseStarted.ToolName;
                _toolArgumentsByCallId[toolUseStarted.ToolCallId] = new StringBuilder();
                _errorWriter.WriteLine(Ansi.Color($"[tool:call:start] {toolUseStarted.ToolName} ({toolUseStarted.ToolCallId})", Ansi.Cyan));
                break;
            case AgentToolUseArgumentsDeltaEvent argsDelta:
                AppendToolArguments(argsDelta);
                break;
            case AgentToolUseCompletedEvent toolUseCompleted:
                EndTextLine();
                RenderToolUseCompleted(toolUseCompleted);
                break;
            case AgentToolExecutionStartedEvent executionStarted:
                EndTextLine();
                _errorWriter.WriteLine(Ansi.Color($"[tool:exec:start] {executionStarted.ToolName} ({executionStarted.ToolCallId})", Ansi.Cyan));
                WriteIndentedBlock("args", TryFormatJson(executionStarted.ArgumentsJson));
                break;
            case AgentToolExecutionUpdatedEvent executionUpdated:
                EndTextLine();
                RenderToolExecutionUpdate(executionUpdated);
                break;
            case AgentToolExecutionCompletedEvent executionCompleted:
                EndTextLine();
                RenderToolResult(executionCompleted);
                _toolNamesByCallId.Remove(executionCompleted.ToolCallId);
                _toolArgumentsByCallId.Remove(executionCompleted.ToolCallId);
                break;
            case AgentErrorEvent error:
                EndTextLine();
                _errorWriter.WriteLine(
                    Ansi.Color($"[error] {error.Message} (category={error.Category}, status={error.StatusCode?.ToString() ?? "n/a"}, retryable={error.Retryable})", Ansi.Red + Ansi.Bold));
                break;
            case AgentTurnCompletedEvent turnCompleted:
                EndTextLine();
                _errorWriter.WriteLine(Ansi.Color($"[turn:end] tool_results={turnCompleted.ToolMessages.Count}", Ansi.Magenta));
                break;
            case AgentCompletedEvent completed:
                if (!_hasTextDeltaInCurrentPrompt)
                    RenderCompletedTextFallback(completed.AssistantMessage);
                EndTextLine();
                _errorWriter.WriteLine(Ansi.Color("[result:end]", Ansi.Green));
                break;
        }
    }

    public void EndTextLine()
    {
        if (!_hasOpenTextLine)
            return;

        _outputWriter.WriteLine();
        _outputWriter.Flush();
        _hasOpenTextLine = false;
    }

    private void RenderAgentStarted(AgentStartedEvent started)
    {
        var mode = started.IsContinuation ? "continue" : "prompt";
        var preview = BuildPreview(started.Prompt ?? string.Empty, 120);

        if (string.Equals(preview, "(empty)", StringComparison.Ordinal))
        {
            _errorWriter.WriteLine(Ansi.Color($"[turn:start] mode={mode}", Ansi.Magenta));
            return;
        }

        _errorWriter.WriteLine(Ansi.Color($"[turn:start] mode={mode} prompt={preview}", Ansi.Magenta));
    }

    private void RenderThinkingCompleted(AgentThinkingCompletedEvent completed)
    {
        if (_thinkingBuffer.Length == 0 && !string.IsNullOrWhiteSpace(completed.FullThinking))
            _thinkingBuffer.Append(completed.FullThinking);

        var preview = BuildPreview(_thinkingBuffer.ToString(), 200);
        _errorWriter.WriteLine(Ansi.Color($"[thinking:end] {preview}", Ansi.Gray));
        _thinkingBuffer.Clear();
    }

    private void AppendToolArguments(AgentToolUseArgumentsDeltaEvent argsDelta)
    {
        if (!_toolArgumentsByCallId.TryGetValue(argsDelta.ToolCallId, out var argsBuilder))
        {
            argsBuilder = new StringBuilder();
            _toolArgumentsByCallId[argsDelta.ToolCallId] = argsBuilder;
        }

        argsBuilder.Append(argsDelta.PartialArgumentsJson);
    }

    private void RenderToolUseCompleted(AgentToolUseCompletedEvent completed)
    {
        var toolName = _toolNamesByCallId.TryGetValue(completed.ToolCallId, out var knownName)
            ? knownName
            : "unknown";

        _errorWriter.WriteLine(Ansi.Color($"[tool:call:ready] {toolName} ({completed.ToolCallId})", Ansi.Cyan));

        if (_toolArgumentsByCallId.TryGetValue(completed.ToolCallId, out var argsBuilder))
        {
            WriteIndentedBlock("args", TryFormatJson(argsBuilder.ToString()));
            return;
        }

        _errorWriter.WriteLine("  args: (empty)");
    }

    private void RenderToolExecutionUpdate(AgentToolExecutionUpdatedEvent updated)
    {
        var preview = BuildPreview(updated.PartialResult.ContentAsText, 180);
        _errorWriter.WriteLine(Ansi.Color($"[tool:exec:update] {updated.ToolName} ({updated.ToolCallId}) => {preview}", Ansi.Cyan));
    }

    private void RenderToolResult(AgentToolExecutionCompletedEvent completed)
    {
        var status = completed.Result.IsError ? "error" : "ok";
        var color = completed.Result.IsError ? Ansi.Red : Ansi.Green;
        _errorWriter.WriteLine(Ansi.Color($"[tool:exec:{status}] {completed.ToolName} ({completed.ToolCallId})", color));
        WriteIndentedBlock("result", BuildToolResultJson(completed.Result));
    }

    private static string BuildToolResultJson(ToolInvocationResult result)
    {
        var payload = new
        {
            isError = result.IsError,
            contentPreview = BuildPreview(result.ContentAsText, 320),
            details = result.Details
        };

        return JsonSerializer.Serialize(payload, PrettyJsonOptions);
    }

    private void WriteIndentedBlock(string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _errorWriter.WriteLine($"  {label}: (empty)");
            return;
        }

        _errorWriter.WriteLine($"  {label}:");
        foreach (var line in value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            _errorWriter.WriteLine($"    {line}");
    }

    private static string TryFormatJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, PrettyJsonOptions);
        }
        catch (JsonException)
        {
            return json.Trim();
        }
    }

    private static string BuildPreview(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "(empty)";

        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (oneLine.Length <= maxLength)
            return oneLine;

        return oneLine[..maxLength] + "...";
    }

    private void RenderCompletedTextFallback(LlmMessage assistantMessage)
    {
        if (assistantMessage.Role != LlmMessageRole.Assistant)
            return;

        var text = string.Concat(
            assistantMessage.Content
                .OfType<TextContentBlock>()
                .Select(block => block.Text));

        if (string.IsNullOrEmpty(text))
            return;

        _outputWriter.Write(text);
        _outputWriter.Flush();
        _hasOpenTextLine = true;
    }
}
