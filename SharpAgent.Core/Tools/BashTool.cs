using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SharpAgent.Core.Tools;

public sealed class BashTool : ITool
{
    private const int DefaultMaxLines = 500;
    private const int DefaultMaxBytes = 100 * 1024; // 100KB
    private const int DefaultTimeoutSeconds = 120;

    public string Name => "bash";
    public string? WorkingDirectory { get; set; }
    public string Description =>
        $"Execute a bash command in the current working directory. Returns stdout and stderr. " +
        $"Output is truncated to last {DefaultMaxLines} lines or {DefaultMaxBytes / 1024}KB (whichever is hit first). " +
        $"If truncated, full output is saved to a temp file.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            command = new { type = "string", description = "The bash command to execute" },
            timeout = new { type = "integer", description = $"Timeout in seconds (default: {DefaultTimeoutSeconds})" }
        },
        required = new[] { "command" }
    };

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var (command, timeout) = ParseInput(input);

            if (string.IsNullOrWhiteSpace(command))
                return ToolResult.Error("command is required", "MISSING_PARAM");

            var shell = GetBashPath();
            var shellArg = "-c";

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = $"{shellArg} \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = WorkingDirectory ?? Environment.CurrentDirectory
            };

            var output = new StringBuilder();
            var outputLock = new object();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    lock (outputLock) output.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    lock (outputLock) output.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return ToolResult.Error($"Command timed out after {timeout} seconds", "TIMEOUT");
            }

            var fullOutput = output.ToString();
            var result = TruncateOutput(fullOutput);

            return ToolResult.Success($"Exit code: {process.ExitCode}\n{result}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error(ex.Message, "EXECUTION_ERROR");
        }
    }

    private static string TruncateOutput(string output)
    {
        if (output.Length <= DefaultMaxBytes)
        {
            var lines = output.Split('\n');
            if (lines.Length <= DefaultMaxLines)
                return output;
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"bash_output_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, output);

        var truncatedLines = output.Split('\n');
        string truncated;

        if (truncatedLines.Length > DefaultMaxLines)
        {
            truncated = string.Join('\n', truncatedLines.TakeLast(DefaultMaxLines));
        }
        else
        {
            truncated = output.Length > DefaultMaxBytes
                ? output[^DefaultMaxBytes..]
                : output;
        }

        return $"[Output truncated. Full output saved to: {tempFile}]\n{truncated}";
    }

    private static (string command, int timeout) ParseInput(string input)
    {
        input = input.Trim();
        if (!input.StartsWith('{'))
            return (input, DefaultTimeoutSeconds);

        using var doc = JsonDocument.Parse(input);
        var root = doc.RootElement;

        var command = root.TryGetProperty("command", out var c) ? c.GetString() ?? "" : "";
        var timeout = root.TryGetProperty("timeout", out var t) && t.TryGetInt32(out var tv)
            ? tv
            : DefaultTimeoutSeconds;

        return (command, timeout);
    }

    private static string GetBashPath()
    {
        if (!OperatingSystem.IsWindows())
            return "/bin/bash";

        string[] gitBashPaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "bin", "bash.exe"),
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe"
        ];

        foreach (var path in gitBashPaths)
        {
            if (File.Exists(path))
                return path;
        }

        return "bash";
    }
}
