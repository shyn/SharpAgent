using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Sharp.Core.Tools;

public sealed class BashTool : IAgentTool
{
    private const int DefaultTimeoutSeconds = 120;
    private const int DefaultMaxLines = 500;
    private const int DefaultMaxBytes = 100 * 1024;

    private readonly string _workingDirectory;

    public BashTool(string workingDirectory)
    {
        _workingDirectory = workingDirectory;
        ParametersSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                command = new { type = "string", description = "Shell command to execute" },
                timeout = new { type = "integer", description = $"Timeout in seconds (default: {DefaultTimeoutSeconds})" }
            },
            required = new[] { "command" }
        }, JsonDefaults.Options);
    }

    public string Name => "bash";

    public string Description =>
        $"Execute shell commands in working directory. Output is truncated to {DefaultMaxLines} lines or {DefaultMaxBytes / 1024}KB.";

    public JsonElement ParametersSchema { get; }

    public async Task<ToolInvocationResult> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        IProgress<ToolInvocationResult>? progress = null,
        CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("command", out var commandProp))
            return ToolInvocationResult.Text("Missing required argument: command", isError: true);

        var command = commandProp.GetString();
        if (string.IsNullOrWhiteSpace(command))
            return ToolInvocationResult.Text("Argument 'command' cannot be empty", isError: true);

        var timeoutSeconds = arguments.TryGetProperty("timeout", out var timeoutProp) && timeoutProp.TryGetInt32(out var timeout)
            ? Math.Max(1, timeout)
            : DefaultTimeoutSeconds;

        var workingDirectory = string.IsNullOrWhiteSpace(context.WorkingDirectory)
            ? _workingDirectory
            : context.WorkingDirectory;

        var shell = OperatingSystem.IsWindows() ? "bash" : "/bin/bash";

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = $"-lc \"{command.Replace("\"", "\\\"")}\"",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var output = new StringBuilder();
        var sync = new object();

        void ReportProgress()
        {
            if (progress == null)
                return;

            string snapshot;
            lock (sync)
            {
                snapshot = output.ToString();
            }

            var tail = Truncate(snapshot, out _);
            progress.Report(ToolInvocationResult.Text(tail));
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
                return;

            lock (sync)
                output.AppendLine(e.Data);

            ReportProgress();
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
                return;

            lock (sync)
                output.AppendLine(e.Data);

            ReportProgress();
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            TryKill(process);
            return ToolInvocationResult.Text($"Command timed out after {timeoutSeconds}s", isError: true);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return ToolInvocationResult.Text("Command aborted", isError: true);
        }

        var content = Truncate(output.ToString(), out var truncatedFile);
        if (truncatedFile != null)
            content = $"[Output truncated. Full output saved to: {truncatedFile}]\n{content}";

        return ToolInvocationResult.Text($"Exit code: {process.ExitCode}\n{content}");
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignored
        }
    }

    private static string Truncate(string output, out string? fullOutputPath)
    {
        fullOutputPath = null;

        var lines = output.Split('\n');
        var bytes = Encoding.UTF8.GetByteCount(output);

        if (lines.Length <= DefaultMaxLines && bytes <= DefaultMaxBytes)
            return output;

        fullOutputPath = Path.Combine(Path.GetTempPath(), $"sharpagent_bash_{Guid.NewGuid():N}.log");
        File.WriteAllText(fullOutputPath, output);

        if (lines.Length > DefaultMaxLines)
            return string.Join('\n', lines.TakeLast(DefaultMaxLines));

        var maxChars = Math.Min(output.Length, DefaultMaxBytes);
        return output[^maxChars..];
    }
}
