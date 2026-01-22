namespace SharpAgent.Core;

/// <summary>
/// Discovers and loads AGENTS.md files following the Codex convention.
/// See https://agents.md for the specification.
/// </summary>
public static class AgentsMdLoader
{
    public const string FileName = "AGENTS.md";
    public const string DefaultCodexHome = ".codex";
    public const string CodexHomeEnvVar = "CODEX_HOME";

    /// <summary>
    /// Loads all applicable AGENTS.md content for the given working directory.
    /// Returns null if no AGENTS.md files are found.
    /// </summary>
    /// <param name="workingDirectory">Current working directory</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Combined AGENTS.md content, or null if none found</returns>
    public static async Task<string?> LoadAsync(string? workingDirectory, CancellationToken ct = default)
    {
        workingDirectory ??= Directory.GetCurrentDirectory();
        var sections = new List<string>();

        // 1. Global config
        var globalPath = GetGlobalAgentsMdPath();
        if (File.Exists(globalPath))
        {
            var content = await File.ReadAllTextAsync(globalPath, ct);
            if (!string.IsNullOrWhiteSpace(content))
                sections.Add($"# [global]\n\n{content.Trim()}");
        }

        // 2. Find project root (git root)
        var gitRoot = FindGitRoot(workingDirectory);

        // 3. Collect paths from project root down to working directory
        foreach (var (path, relativePath) in CollectAgentsMdPaths(workingDirectory, gitRoot))
        {
            if (File.Exists(path))
            {
                var content = await File.ReadAllTextAsync(path, ct);
                if (!string.IsNullOrWhiteSpace(content))
                    sections.Add($"# [{relativePath}]\n\n{content.Trim()}");
            }
        }

        return sections.Count > 0 ? string.Join("\n\n---\n\n", sections) : null;
    }

    /// <summary>
    /// Gets the global AGENTS.md path ($CODEX_HOME or ~/.codex).
    /// </summary>
    public static string GetGlobalAgentsMdPath()
    {
        var codexHome = Environment.GetEnvironmentVariable(CodexHomeEnvVar);
        if (string.IsNullOrEmpty(codexHome))
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            codexHome = Path.Combine(userHome, DefaultCodexHome);
        }
        return Path.Combine(codexHome, FileName);
    }

    /// <summary>
    /// Finds the git repository root for the given directory.
    /// Returns null if not in a git repository.
    /// </summary>
    public static string? FindGitRoot(string? directory)
    {
        if (string.IsNullOrEmpty(directory)) return null;

        var dir = new DirectoryInfo(directory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// Collects all AGENTS.md paths from project root to working directory.
    /// </summary>
    public static IEnumerable<(string Path, string RelativePath)> CollectAgentsMdPaths(
        string? workingDirectory,
        string? projectRoot)
    {
        if (string.IsNullOrEmpty(workingDirectory)) yield break;

        workingDirectory = Path.GetFullPath(workingDirectory);

        // If no project root, only check current directory
        if (string.IsNullOrEmpty(projectRoot))
        {
            var cwdPath = Path.Combine(workingDirectory, FileName);
            yield return (cwdPath, ".");
            yield break;
        }

        projectRoot = Path.GetFullPath(projectRoot);

        // Build path from project root to working directory
        var relativePath = Path.GetRelativePath(projectRoot, workingDirectory);
        var pathSegments = relativePath == "."
            ? Array.Empty<string>()
            : relativePath.Split(Path.DirectorySeparatorChar);

        // Start at project root
        var currentDir = projectRoot;
        yield return (Path.Combine(currentDir, FileName), ".");

        // Walk down to working directory
        foreach (var segment in pathSegments)
        {
            currentDir = Path.Combine(currentDir, segment);
            var relPath = Path.GetRelativePath(projectRoot, currentDir);
            yield return (Path.Combine(currentDir, FileName), relPath);
        }
    }

    /// <summary>
    /// Combines the base system prompt with AGENTS.md content.
    /// </summary>
    public static string BuildSystemPrompt(string basePrompt, string? agentsMdContent)
    {
        if (string.IsNullOrWhiteSpace(agentsMdContent))
            return basePrompt;

        return $"{basePrompt}\n\n<agents_md>\n{agentsMdContent}\n</agents_md>";
    }
}
