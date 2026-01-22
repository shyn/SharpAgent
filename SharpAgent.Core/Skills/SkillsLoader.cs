using System.Text.RegularExpressions;

namespace SharpAgent.Core.Skills;

/// <summary>
/// Discovers and loads Agent Skills from configured directories.
/// See https://agentskills.io/specification for the full specification.
/// </summary>
public static partial class SkillsLoader
{
    public const string SkillFileName = "SKILL.md";

    /// <summary>
    /// Discovers all valid skills in the given directories.
    /// </summary>
    public static async Task<IReadOnlyList<SkillMetadata>> DiscoverAsync(
        IEnumerable<(string Directory, bool Recursive)> skillDirectories,
        CancellationToken ct = default)
    {
        var skills = new List<SkillMetadata>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (baseDir, recursive) in skillDirectories)
        {
            if (!Directory.Exists(baseDir))
                continue;

            var skillPaths = recursive
                ? FindSkillsRecursive(baseDir)
                : FindSkillsFlat(baseDir);

            foreach (var skillPath in skillPaths)
            {
                var skill = await ParseSkillAsync(skillPath, ct);
                if (skill != null)
                {
                    // Validate that name matches parent directory name
                    var dirName = Path.GetFileName(Path.GetDirectoryName(skillPath)!);
                    if (skill.Name == dirName && seenNames.Add(skill.Name))
                    {
                        skills.Add(skill);
                    }
                }
            }
        }

        return skills;
    }

    /// <summary>
    /// Discovers all valid skills in the given directories (simple overload for non-recursive).
    /// </summary>
    public static Task<IReadOnlyList<SkillMetadata>> DiscoverAsync(
        IEnumerable<string> skillDirectories,
        CancellationToken ct = default)
    {
        return DiscoverAsync(skillDirectories.Select(d => (d, false)), ct);
    }

    private static IEnumerable<string> FindSkillsFlat(string baseDir)
    {
        foreach (var skillDir in Directory.GetDirectories(baseDir))
        {
            var skillPath = Path.Combine(skillDir, SkillFileName);
            if (File.Exists(skillPath))
                yield return skillPath;
        }
    }

    private static IEnumerable<string> FindSkillsRecursive(string baseDir)
    {
        var searchOption = SearchOption.AllDirectories;
        string[] skillFiles;

        try
        {
            skillFiles = Directory.GetFiles(baseDir, SkillFileName, searchOption);
        }
        catch (DirectoryNotFoundException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var skillPath in skillFiles)
        {
            yield return skillPath;
        }
    }

    /// <summary>
    /// Parses a single SKILL.md file and extracts metadata and body.
    /// </summary>
    public static async Task<SkillMetadata?> ParseSkillAsync(string skillPath, CancellationToken ct = default)
    {
        if (!File.Exists(skillPath))
            return null;

        var content = await File.ReadAllTextAsync(skillPath, ct);
        return ParseSkill(content, skillPath);
    }

    /// <summary>
    /// Parses SKILL.md content and extracts frontmatter and body.
    /// </summary>
    public static SkillMetadata? ParseSkill(string content, string location)
    {
        var (frontmatter, body) = ExtractFrontmatter(content);
        if (frontmatter == null)
            return null;

        var fields = ParseYamlFrontmatter(frontmatter);

        if (!fields.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return null;
        if (!fields.TryGetValue("description", out var description) || string.IsNullOrWhiteSpace(description))
            return null;

        // Validate name format
        if (!IsValidSkillName(name))
            return null;

        fields.TryGetValue("license", out var license);
        fields.TryGetValue("compatibility", out var compatibility);
        fields.TryGetValue("allowed-tools", out var allowedTools);

        // Parse metadata block if present
        Dictionary<string, string>? metadata = null;
        if (fields.TryGetValue("metadata", out var metadataRaw) && !string.IsNullOrWhiteSpace(metadataRaw))
        {
            metadata = ParseMetadataBlock(metadataRaw);
        }

        return new SkillMetadata
        {
            Name = name,
            Description = description,
            License = license,
            Compatibility = compatibility,
            AllowedTools = allowedTools,
            Metadata = metadata,
            Location = Path.GetFullPath(location),
            Body = body.Trim()
        };
    }

    /// <summary>
    /// Validates that a skill name follows the specification rules.
    /// </summary>
    public static bool IsValidSkillName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 64)
            return false;
        if (name.StartsWith('-') || name.EndsWith('-'))
            return false;
        if (name.Contains("--"))
            return false;

        return SkillNameRegex().IsMatch(name);
    }

    /// <summary>
    /// Builds the skills section for injection into the system prompt.
    /// Includes usage instructions and available skills list.
    /// </summary>
    public static string BuildAvailableSkillsPrompt(IReadOnlyList<SkillMetadata> skills)
    {
        if (skills.Count == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();

        // Skills introduction and usage instructions
        sb.AppendLine("""
            # Skills

            Skills are self-contained capability packages that you can load on-demand.
            A skill provides specialized workflows, setup instructions, helper scripts, and reference documentation for specific tasks.

            When you recognize a task that matches a skill's description, use the `read_file` tool to read the skill's SKILL.md file (specified in the <location> field).
            The file contains the full instructions for completing the task.

            ## Available Skills
            """);

        // List available skills
        sb.AppendLine();
        sb.AppendLine("<available_skills>");

        foreach (var skill in skills)
        {
            sb.AppendLine("  <skill>");
            sb.AppendLine($"    <name>{EscapeXml(skill.Name)}</name>");
            sb.AppendLine($"    <description>{EscapeXml(skill.Description)}</description>");
            sb.AppendLine($"    <location>{EscapeXml(skill.Location)}</location>");
            sb.AppendLine("  </skill>");
        }

        sb.AppendLine("</available_skills>");
        return sb.ToString();
    }

    private static (string? Frontmatter, string Body) ExtractFrontmatter(string content)
    {
        var lines = content.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
            return (null, content);

        var endIndex = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                endIndex = i;
                break;
            }
        }

        if (endIndex == -1)
            return (null, content);

        var frontmatter = string.Join('\n', lines[1..endIndex]);
        var body = string.Join('\n', lines[(endIndex + 1)..]);
        return (frontmatter, body);
    }

    private static Dictionary<string, string> ParseYamlFrontmatter(string yaml)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = yaml.Split('\n');
        string? currentKey = null;
        var valueBuilder = new System.Text.StringBuilder();
        var inMultilineBlock = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();

            // Check for new key-value pair (not indented)
            if (!string.IsNullOrEmpty(trimmed) && !char.IsWhiteSpace(trimmed[0]) && trimmed.Contains(':'))
            {
                // Save previous key if any
                if (currentKey != null)
                {
                    result[currentKey] = valueBuilder.ToString().Trim();
                }

                var colonIndex = trimmed.IndexOf(':');
                currentKey = trimmed[..colonIndex].Trim();
                var value = trimmed[(colonIndex + 1)..].Trim();

                valueBuilder.Clear();
                if (!string.IsNullOrEmpty(value))
                {
                    valueBuilder.Append(value);
                    inMultilineBlock = false;
                }
                else
                {
                    // Value might be on next lines (multiline or mapping)
                    inMultilineBlock = true;
                }
            }
            else if (currentKey != null && inMultilineBlock && !string.IsNullOrWhiteSpace(line))
            {
                // Continuation of multiline value
                if (valueBuilder.Length > 0)
                    valueBuilder.AppendLine();
                valueBuilder.Append(line);
            }
        }

        // Save last key
        if (currentKey != null)
        {
            result[currentKey] = valueBuilder.ToString().Trim();
        }

        return result;
    }

    private static Dictionary<string, string> ParseMetadataBlock(string metadataRaw)
    {
        var result = new Dictionary<string, string>();
        var lines = metadataRaw.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = trimmed[..colonIndex].Trim();
                var value = trimmed[(colonIndex + 1)..].Trim().Trim('"');
                result[key] = value;
            }
        }

        return result;
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex SkillNameRegex();
}
