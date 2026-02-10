using Sharp.Core.Skills;

namespace Sharp.Core.Resources;

public enum ResourceDiagnosticSeverity
{
    Warning,
    Error
}

public sealed record ResourceDiagnostic(
    ResourceDiagnosticSeverity Severity,
    string Message,
    string? Path = null);

public sealed record ContextFile(
    string Path,
    string Content);

public sealed record SessionResourceOptions(
    string WorkingDirectory,
    string AgentDirectory,
    string BaseSystemPrompt,
    string? AppendSystemPrompt = null,
    bool DiscoverSystemPromptFile = true,
    bool IncludeProjectContextFiles = true,
    bool EnableSkills = true,
    bool IncludeDefaultSkills = true,
    IReadOnlyList<string>? SkillPaths = null);

public sealed record SessionResourceLoadResult(
    string BaseSystemPrompt,
    IReadOnlyList<string> AppendSystemPromptSections,
    IReadOnlyList<ContextFile> ContextFiles,
    IReadOnlyList<Skill> Skills,
    IReadOnlyList<SkillDiagnostic> SkillDiagnostics,
    IReadOnlyList<ResourceDiagnostic> Diagnostics);

public sealed record SessionResourceSnapshot(
    string BaseSystemPrompt,
    string FinalSystemPrompt,
    IReadOnlyList<string> AppendSystemPromptSections,
    IReadOnlyList<ContextFile> ContextFiles,
    IReadOnlyList<Skill> Skills,
    IReadOnlyList<SkillDiagnostic> SkillDiagnostics,
    IReadOnlyList<ResourceDiagnostic> Diagnostics)
{
    public static SessionResourceSnapshot Empty(string systemPrompt)
        => new(
            BaseSystemPrompt: systemPrompt,
            FinalSystemPrompt: systemPrompt,
            AppendSystemPromptSections: Array.Empty<string>(),
            ContextFiles: Array.Empty<ContextFile>(),
            Skills: Array.Empty<Skill>(),
            SkillDiagnostics: Array.Empty<SkillDiagnostic>(),
            Diagnostics: Array.Empty<ResourceDiagnostic>());
}
