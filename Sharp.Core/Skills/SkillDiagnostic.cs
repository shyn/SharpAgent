namespace Sharp.Core.Skills;

/// <summary>
/// Types of skill loading diagnostics.
/// </summary>
public enum SkillDiagnosticType
{
    /// <summary>Non-fatal issue during skill loading.</summary>
    Warning,

    /// <summary>Name collision between two skills.</summary>
    Collision
}

/// <summary>
/// Information about a name collision between skills.
/// </summary>
/// <param name="Name">The conflicting skill name</param>
/// <param name="WinnerPath">Path of the skill that was kept</param>
/// <param name="LoserPath">Path of the skill that was discarded</param>
public sealed record SkillCollision(
    string Name,
    string WinnerPath,
    string LoserPath);

/// <summary>
/// Diagnostic information from skill loading.
/// </summary>
/// <param name="Type">Diagnostic severity/type</param>
/// <param name="Message">Human-readable diagnostic message</param>
/// <param name="Path">File path related to this diagnostic</param>
/// <param name="Collision">Collision details if Type is Collision</param>
public sealed record SkillDiagnostic(
    SkillDiagnosticType Type,
    string Message,
    string Path,
    SkillCollision? Collision = null);
