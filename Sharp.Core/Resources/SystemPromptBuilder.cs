using System.Text;
using Sharp.Core.Skills;

namespace Sharp.Core.Resources;

/// <summary>
/// Composes final system prompt using base prompt, append sections, context files, and skills.
/// </summary>
public static class SystemPromptBuilder
{
    public static string Build(
        string baseSystemPrompt,
        IEnumerable<string> appendSystemPromptSections,
        IEnumerable<ContextFile> contextFiles,
        IEnumerable<Skill> skills,
        bool includeSkills)
    {
        var builder = new StringBuilder(baseSystemPrompt.TrimEnd());

        foreach (var section in appendSystemPromptSections.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append(section.TrimEnd());
        }

        var contextList = contextFiles.ToList();
        if (contextList.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("# Project Context");
            builder.AppendLine();
            builder.AppendLine("Project-specific instructions and guidelines:");

            foreach (var contextFile in contextList)
            {
                builder.AppendLine();
                builder.AppendLine($"## {contextFile.Path}");
                builder.AppendLine();
                builder.Append(contextFile.Content.TrimEnd());
                builder.AppendLine();
            }
        }

        if (includeSkills)
        {
            var skillSection = SkillPromptFormatter.FormatForPrompt(skills);
            if (!string.IsNullOrEmpty(skillSection))
                builder.Append(skillSection);
        }

        return builder.ToString();
    }
}
