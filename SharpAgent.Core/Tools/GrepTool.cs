using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SharpAgent.Core.Tools;

public sealed class GrepTool : ITool
{
    private const int DefaultMaxLines = 500;
    private const int DefaultMaxBytes = 100 * 1024; // 100KB
    private const int DefaultTimeoutSeconds = 60;

    public string Name => "grep";
    public string? WorkingDirectory { get; set; }
    public string Description =>
        "Search for text patterns in files using ripgrep (rg) if available, otherwise falling back to grep. " +
        "The 'pattern' parameter is REQUIRED. Supports regex by default, case-insensitive search, and file filtering.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            pattern = new { type = "string", description = "The pattern to search for (REQUIRED)" },
            path = new { type = "string", description = "The directory or file to search in (default: \".\")" },
            isRegex = new { type = "boolean", description = "Whether to treat the pattern as a regular expression (default: true)" },
            caseInsensitive = new { type = "boolean", description = "Whether to perform a case-insensitive search (default: false)" },
            include = new { type = "string", description = "Glob pattern for files to include (e.g., \"*.cs\")" }
        },
        required = new[] { "pattern" }
    };

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var (pattern, path, isRegex, caseInsensitive, include) = ParseInput(input);

            if (string.IsNullOrWhiteSpace(pattern))
                return ToolResult.Error("'pattern' parameter is required. Please provide a search pattern as a string (e.g., {\"pattern\": \"searchTerm\", \"path\": \".\"}))", "MISSING_PARAM");

            var (exe, args) = PrepareCommand(pattern, path, isRegex, caseInsensitive, include);

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory
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

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return ToolResult.Error($"Grep timed out after {DefaultTimeoutSeconds} seconds", "TIMEOUT");
            }

            var fullOutput = output.ToString();
            
            if (string.IsNullOrWhiteSpace(fullOutput) && process.ExitCode != 0)
            {
                return ToolResult.Success($"No matches found (Exit code: {process.ExitCode})");
            }

            var result = TruncateOutput(fullOutput);
            return ToolResult.Success(result);
        }
        catch (Exception ex)
        {
            return ToolResult.Error(ex.Message, "EXECUTION_ERROR");
        }
    }

    private static (string pattern, string path, bool isRegex, bool caseInsensitive, string? include) ParseInput(string input)
    {
        input = input.Trim();
        if (!input.StartsWith('{'))
            return (input, ".", true, false, null);

        using var doc = JsonDocument.Parse(input);
        var root = doc.RootElement;

        var pattern = root.TryGetProperty("pattern", out var p) ? p.GetString() ?? "" : "";
        var path = root.TryGetProperty("path", out var pathProp) ? pathProp.GetString() ?? "." : ".";
        var isRegex = !root.TryGetProperty("isRegex", out var ir) || ir.GetBoolean();
        var caseInsensitive = root.TryGetProperty("caseInsensitive", out var ci) && ci.GetBoolean();
        var include = root.TryGetProperty("include", out var inc) ? inc.GetString() : null;

        return (pattern, path, isRegex, caseInsensitive, include);
    }

    private static (string exe, string args) PrepareCommand(string pattern, string path, bool isRegex, bool caseInsensitive, string? include)
    {
        bool hasRg = CanRun("rg");
        string exe = hasRg ? "rg" : (OperatingSystem.IsWindows() ? GetGrepPath() : "grep");

        var args = new StringBuilder();
        
        if (hasRg)
        {
            // rg defaults to regex
            if (!isRegex) args.Append("-F ");
            if (caseInsensitive) args.Append("-i ");
            if (!string.IsNullOrEmpty(include)) args.Append($"-g \"{include}\" ");
            
            // Add line numbers, with-filename, and color none for cleaner output
            args.Append("--line-number --with-filename --color never ");
            args.Append($"\"{pattern.Replace("\"", "\\\"")}\" \"{path}\"");
        }
        else
        {
            // grep fallback
            args.Append("-r "); // recursive
            if (!isRegex) args.Append("-F ");
            if (caseInsensitive) args.Append("-i ");
            if (!string.IsNullOrEmpty(include)) args.Append($"--include=\"{include}\" ");
            
            args.Append("-nH "); // line numbers and filename
            args.Append($"\"{pattern.Replace("\"", "\\\"")}\" \"{path}\"");
        }

        return (exe, args.ToString());
    }

    private static bool CanRun(string command)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where" : "which",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetGrepPath()
    {
        // On Windows, try to find grep in Git installation
        string[] grepPaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "usr", "bin", "grep.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "usr", "bin", "grep.exe"),
            @"C:\Program Files\Git\usr\bin\grep.exe",
            @"C:\Program Files (x86)\Git\usr\bin\grep.exe"
        ];

        foreach (var path in grepPaths)
        {
            if (File.Exists(path))
                return path;
        }

        return "grep";
    }

    private static string TruncateOutput(string output)
    {
        if (output.Length <= DefaultMaxBytes)
        {
            var lines = output.Split('\n');
            if (lines.Length <= DefaultMaxLines)
                return output;
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"grep_output_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, output);

        var truncatedLines = output.Split('\n');
        string truncated;

        if (truncatedLines.Length > DefaultMaxLines)
        {
            truncated = string.Join('\n', truncatedLines.Take(DefaultMaxLines));
            truncated += $"\n... plus {truncatedLines.Length - DefaultMaxLines} more lines";
        }
        else
        {
            truncated = output.Length > DefaultMaxBytes
                ? output[..DefaultMaxBytes]
                : output;
            truncated += "\n... output truncated due to size";
        }

        return $"[Matches found. Full output saved to: {tempFile}]\n{truncated}";
    }
}
