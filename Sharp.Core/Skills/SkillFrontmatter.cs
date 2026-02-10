using System.Text.RegularExpressions;

namespace Sharp.Core.Skills;

/// <summary>
/// Parsed frontmatter data from a skill file.
/// </summary>
/// <param name="Name">Optional skill name override</param>
/// <param name="Description">Required skill description</param>
/// <param name="DisableModelInvocation">If true, skill is hidden from LLM</param>
public sealed record SkillFrontmatterData(
    string? Name,
    string? Description,
    bool DisableModelInvocation);

/// <summary>
/// Parses YAML frontmatter from skill markdown files.
/// Uses simple regex parsing to avoid external dependencies.
/// </summary>
public static partial class SkillFrontmatter
{
    /// <summary>
    /// Parse frontmatter from markdown content.
    /// Frontmatter must be at the start of the file, delimited by --- lines.
    /// </summary>
    /// <param name="content">Raw markdown file content</param>
    /// <returns>Parsed frontmatter data and remaining body content</returns>
    public static (SkillFrontmatterData? Data, string Body) Parse(string content)
    {
        if (string.IsNullOrEmpty(content))
            return (null, string.Empty);

        var normalized = content.Replace("\r\n", "\n").Replace("\r", "\n");

        if (!normalized.StartsWith("---"))
            return (null, normalized);

        var endIndex = normalized.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (endIndex < 0)
            return (null, normalized);

        var yamlContent = normalized[3..endIndex].Trim();
        var body = normalized[(endIndex + 4)..].TrimStart('\n');

        var data = ParseYaml(yamlContent);
        return (data, body);
    }

    /// <summary>
    /// Extract just the body content, stripping any frontmatter.
    /// </summary>
    public static string StripFrontmatter(string content)
        => Parse(content).Body;

    private static SkillFrontmatterData ParseYaml(string yaml)
    {
        string? name = null;
        string? description = null;
        var disableModelInvocation = false;

        foreach (var line in yaml.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex < 0)
                continue;

            var key = trimmed[..colonIndex].Trim().ToLowerInvariant();
            var value = trimmed[(colonIndex + 1)..].Trim();

            // Remove surrounding quotes if present
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            switch (key)
            {
                case "name":
                    name = value;
                    break;
                case "description":
                    description = value;
                    break;
                case "disable-model-invocation":
                    disableModelInvocation = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        return new SkillFrontmatterData(name, description, disableModelInvocation);
    }
}
