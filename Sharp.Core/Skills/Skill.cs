namespace Sharp.Core.Skills;

/// <summary>
/// Represents a loaded skill with its metadata.
/// </summary>
/// <param name="Name">Skill name (lowercase, hyphens only, max 64 chars)</param>
/// <param name="Description">Human-readable description (max 1024 chars)</param>
/// <param name="FilePath">Absolute path to the skill file (SKILL.md or .md)</param>
/// <param name="BaseDir">Directory containing the skill file</param>
/// <param name="Source">Origin of the skill: "user", "project", or "path"</param>
/// <param name="DisableModelInvocation">If true, skill is hidden from LLM and only invokable explicitly</param>
public sealed record Skill(
    string Name,
    string Description,
    string FilePath,
    string BaseDir,
    string Source,
    bool DisableModelInvocation);
