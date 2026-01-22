namespace SharpAgent.Core;

public sealed class AgentOptions
{
    public string? WorkingDirectory { get; init; }
    public string SystemPrompt { get; init; } = "You are a helpful assistant. Use tools when needed to accomplish tasks.";
    public int MaxIterations { get; init; } = 20;
    
    /// <summary>
    /// Whether to automatically discover and load AGENTS.md files.
    /// Default is true.
    /// </summary>
    public bool LoadAgentsMd { get; init; } = true;

    /// <summary>
    /// Whether to automatically discover and load skills.
    /// Default is true.
    /// </summary>
    public bool LoadSkills { get; init; } = true;

    /// <summary>
    /// Directories to scan for skills. Each subdirectory containing a SKILL.md file is treated as a skill.
    /// If null, defaults to ~/.config/agents/skills
    /// </summary>
    public IReadOnlyList<string>? SkillDirectories { get; init; }

    /// <summary>
    /// Gets the skill directories to scan, including default locations.
    /// Returns tuples of (directory, recursive).
    /// </summary>
    public IEnumerable<(string Directory, bool Recursive)> GetEffectiveSkillDirectories()
    {
        // Custom directories (non-recursive by default)
        if (SkillDirectories != null)
        {
            foreach (var dir in SkillDirectories)
                yield return (dir, false);
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Project-local: .agents/skills/** (recursive)
        if (WorkingDirectory != null)
        {
            yield return (Path.Combine(WorkingDirectory, ".agents", "skills"), true);
            // Claude Code project-local: .claude/skills/*
            yield return (Path.Combine(WorkingDirectory, ".claude", "skills"), false);
        }

        // Claude Code user: ~/.claude/skills/*
        yield return (Path.Combine(userHome, ".claude", "skills"), false);

        // Codex CLI: ~/.codex/skills/** (recursive)
        yield return (Path.Combine(userHome, ".codex", "skills"), true);

        // Agent Skills standard: ~/.config/agents/skills
        yield return (Path.Combine(userHome, ".config", "agents", "skills"), false);
    }
}
