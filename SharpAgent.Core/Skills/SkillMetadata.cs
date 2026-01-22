namespace SharpAgent.Core.Skills;

/// <summary>
/// Represents the metadata parsed from a SKILL.md frontmatter.
/// See https://agentskills.io/specification for the full specification.
/// </summary>
public sealed record SkillMetadata
{
    /// <summary>
    /// Required. Must be 1-64 characters, lowercase alphanumeric and hyphens only.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Required. Must be 1-1024 characters. Describes what the skill does and when to use it.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Optional. License name or reference to a bundled license file.
    /// </summary>
    public string? License { get; init; }

    /// <summary>
    /// Optional. Max 500 characters. Indicates environment requirements.
    /// </summary>
    public string? Compatibility { get; init; }

    /// <summary>
    /// Optional. Arbitrary key-value mapping for additional metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Optional. Space-delimited list of pre-approved tools the skill may use.
    /// </summary>
    public string? AllowedTools { get; init; }

    /// <summary>
    /// The absolute path to the SKILL.md file.
    /// </summary>
    public required string Location { get; init; }

    /// <summary>
    /// The full body content (markdown instructions) of the skill.
    /// </summary>
    public required string Body { get; init; }
}
