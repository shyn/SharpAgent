using Sharp.Core.Skills;

namespace Sharp.Core.Resources;

/// <summary>
/// Loads runtime resources that contribute to system prompt construction.
/// </summary>
public sealed class SessionResourceLoader
{
    private const string ConfigDirName = ".sharp";
    private static readonly string[] ContextFileCandidates = ["AGENTS.md", "CLAUDE.md"];

    public SessionResourceLoadResult Load(SessionResourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<ResourceDiagnostic>();
        var cwd = Path.GetFullPath(options.WorkingDirectory);
        var agentDir = Path.GetFullPath(options.AgentDirectory);

        var basePrompt = ResolveBaseSystemPrompt(options, cwd, agentDir, diagnostics);
        var appendSections = LoadAppendPromptSections(options.AppendSystemPrompt, cwd, agentDir, diagnostics);

        var contextFiles = options.IncludeProjectContextFiles
            ? LoadContextFiles(cwd, agentDir, diagnostics)
            : Array.Empty<ContextFile>();

        var skillLoad = options.EnableSkills
            ? LoadSkills(options, cwd, agentDir)
            : new SkillLoadResult(Array.Empty<Skill>(), Array.Empty<SkillDiagnostic>());

        diagnostics.AddRange(skillLoad.Diagnostics.Select(diagnostic =>
            new ResourceDiagnostic(
                Severity: ResourceDiagnosticSeverity.Warning,
                Message: diagnostic.Message,
                Path: diagnostic.Path)));

        return new SessionResourceLoadResult(
            BaseSystemPrompt: basePrompt,
            AppendSystemPromptSections: appendSections,
            ContextFiles: contextFiles,
            Skills: skillLoad.Skills,
            SkillDiagnostics: skillLoad.Diagnostics,
            Diagnostics: diagnostics);
    }

    private static string ResolveBaseSystemPrompt(
        SessionResourceOptions options,
        string cwd,
        string agentDir,
        List<ResourceDiagnostic> diagnostics)
    {
        if (options.DiscoverSystemPromptFile)
        {
            var discovered = DiscoverSystemPromptFile(cwd, agentDir);
            if (!string.IsNullOrWhiteSpace(discovered))
            {
                var discoveredPrompt = ResolvePromptInput(
                    discovered,
                    cwd,
                    diagnostics,
                    fallbackToLiteralWhenMissingFile: false);

                if (!string.IsNullOrWhiteSpace(discoveredPrompt))
                    return discoveredPrompt;
            }
        }

        var resolvedBasePrompt = ResolvePromptInput(
            options.BaseSystemPrompt,
            cwd,
            diagnostics,
            fallbackToLiteralWhenMissingFile: true);

        return string.IsNullOrWhiteSpace(resolvedBasePrompt)
            ? options.BaseSystemPrompt
            : resolvedBasePrompt;
    }

    private static IReadOnlyList<string> LoadAppendPromptSections(
        string? appendSystemPrompt,
        string cwd,
        string agentDir,
        List<ResourceDiagnostic> diagnostics)
    {
        var sections = new List<string>();

        if (!string.IsNullOrWhiteSpace(appendSystemPrompt))
        {
            var resolved = ResolvePromptInput(
                appendSystemPrompt,
                cwd,
                diagnostics,
                fallbackToLiteralWhenMissingFile: true);
            if (!string.IsNullOrWhiteSpace(resolved))
                sections.Add(resolved);
            return sections;
        }

        var discoveredPath = DiscoverAppendSystemPromptFile(cwd, agentDir);
        if (discoveredPath == null)
            return sections;

        var discovered = ResolvePromptInput(
            discoveredPath,
            cwd,
            diagnostics,
            fallbackToLiteralWhenMissingFile: false);
        if (!string.IsNullOrWhiteSpace(discovered))
            sections.Add(discovered);

        return sections;
    }

    private static IReadOnlyList<ContextFile> LoadContextFiles(
        string cwd,
        string agentDir,
        List<ResourceDiagnostic> diagnostics)
    {
        var contextFiles = new List<ContextFile>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var global = LoadContextFileFromDirectory(agentDir, diagnostics);
        if (global != null && seenPaths.Add(global.Path))
            contextFiles.Add(global);

        var stack = new Stack<ContextFile>();
        var currentDir = cwd;
        while (true)
        {
            var contextFile = LoadContextFileFromDirectory(currentDir, diagnostics);
            if (contextFile != null && seenPaths.Add(contextFile.Path))
                stack.Push(contextFile);

            var parent = Directory.GetParent(currentDir)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, currentDir, StringComparison.Ordinal))
                break;

            currentDir = parent;
        }

        while (stack.Count > 0)
            contextFiles.Add(stack.Pop());

        return contextFiles;
    }

    private static ContextFile? LoadContextFileFromDirectory(
        string dir,
        List<ResourceDiagnostic> diagnostics)
    {
        foreach (var candidate in ContextFileCandidates)
        {
            var path = Path.Combine(dir, candidate);
            if (!File.Exists(path))
                continue;

            try
            {
                return new ContextFile(Path.GetFullPath(path), File.ReadAllText(path));
            }
            catch
            {
                diagnostics.Add(new ResourceDiagnostic(
                    Severity: ResourceDiagnosticSeverity.Warning,
                    Message: "Failed to read context file",
                    Path: Path.GetFullPath(path)));
            }
        }

        return null;
    }

    private static string? DiscoverSystemPromptFile(string cwd, string agentDir)
    {
        var project = Path.Combine(cwd, ConfigDirName, "SYSTEM.md");
        if (File.Exists(project))
            return project;

        var global = Path.Combine(agentDir, "SYSTEM.md");
        if (File.Exists(global))
            return global;

        return null;
    }

    private static string? DiscoverAppendSystemPromptFile(string cwd, string agentDir)
    {
        var project = Path.Combine(cwd, ConfigDirName, "APPEND_SYSTEM.md");
        if (File.Exists(project))
            return project;

        var global = Path.Combine(agentDir, "APPEND_SYSTEM.md");
        if (File.Exists(global))
            return global;

        return null;
    }

    private static SkillLoadResult LoadSkills(
        SessionResourceOptions options,
        string cwd,
        string agentDir)
    {
        var loader = new SkillLoader();
        return loader.Load(new SkillLoadOptions(
            Cwd: cwd,
            AgentDir: agentDir,
            SkillPaths: options.SkillPaths,
            IncludeDefaults: options.IncludeDefaultSkills));
    }

    private static string? ResolvePromptInput(
        string? input,
        string cwd,
        List<ResourceDiagnostic> diagnostics,
        bool fallbackToLiteralWhenMissingFile)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var trimmed = input.Trim();
        var resolvedPath = Path.IsPathRooted(trimmed)
            ? trimmed
            : Path.GetFullPath(Path.Combine(cwd, trimmed));

        if (!File.Exists(resolvedPath))
            return fallbackToLiteralWhenMissingFile ? trimmed : null;

        try
        {
            return File.ReadAllText(resolvedPath);
        }
        catch
        {
            diagnostics.Add(new ResourceDiagnostic(
                Severity: ResourceDiagnosticSeverity.Warning,
                Message: "Failed to read prompt input file",
                Path: resolvedPath));
            return null;
        }
    }
}
