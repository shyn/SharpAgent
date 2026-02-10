using System.Text;

namespace Sharp.Core.Skills;

/// <summary>
/// Formats skills for inclusion in LLM system prompts.
/// Uses XML format per Agent Skills standard.
/// </summary>
public static class SkillPromptFormatter
{
    /// <summary>
    /// Format skills as XML for system prompt injection.
    /// Skills with DisableModelInvocation=true are excluded.
    /// </summary>
    /// <param name="skills">Skills to format</param>
    /// <returns>XML string to append to system prompt, or empty if no visible skills</returns>
    public static string FormatForPrompt(IEnumerable<Skill> skills)
    {
        var visibleSkills = skills.Where(s => !s.DisableModelInvocation).ToList();

        if (visibleSkills.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("The following skills provide specialized instructions for specific tasks.");
        sb.AppendLine("Use the read tool to load a skill's file when the task matches its description.");
        sb.AppendLine("When a skill file references a relative path, resolve it against the skill directory (parent of SKILL.md / dirname of the path) and use that absolute path in tool commands.");
        sb.AppendLine();
        sb.AppendLine("<available_skills>");

        foreach (var skill in visibleSkills)
        {
            sb.AppendLine("  <skill>");
            sb.AppendLine($"    <name>{EscapeXml(skill.Name)}</name>");
            sb.AppendLine($"    <description>{EscapeXml(skill.Description)}</description>");
            sb.AppendLine($"    <location>{EscapeXml(skill.FilePath)}</location>");
            sb.AppendLine("  </skill>");
        }

        sb.Append("</available_skills>");

        return sb.ToString();
    }

    private static string EscapeXml(string str)
        => str
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
}
