using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sharp.Core.Configuration;

public static class ConfigValueResolver
{
    private static readonly ConcurrentDictionary<string, string?> CommandCache = new(StringComparer.Ordinal);

    public static string? Resolve(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;

        if (raw.StartsWith('!'))
            return ExecuteCommand(raw);

        var envValue = Environment.GetEnvironmentVariable(raw);
        return !string.IsNullOrEmpty(envValue) ? envValue : raw;
    }

    public static IReadOnlyDictionary<string, string>? ResolveHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0)
            return null;

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in headers)
        {
            var resolvedValue = Resolve(value);
            if (resolvedValue is not null)
                resolved[key] = resolvedValue;
        }

        return resolved.Count > 0 ? resolved : null;
    }

    public static void ClearCache() => CommandCache.Clear();

    private static string? ExecuteCommand(string commandConfig)
    {
        return CommandCache.GetOrAdd(commandConfig, static key =>
        {
            var command = key[1..];
            try
            {
                var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = isWindows ? "cmd.exe" : "/bin/sh",
                    ArgumentList = { isWindows ? "/c" : "-c", command },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process.Start();
                process.StandardInput.Close();

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(TimeSpan.FromSeconds(10));

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    return null;
                }

                var trimmed = output.Trim();
                return trimmed.Length > 0 ? trimmed : null;
            }
            catch
            {
                return null;
            }
        });
    }
}
