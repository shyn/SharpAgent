using System.Text.RegularExpressions;

namespace Sharp.Core.Skills;

/// <summary>
/// Options for loading skills.
/// </summary>
/// <param name="Cwd">Working directory for project-local skills. Default: current directory</param>
/// <param name="AgentDir">Agent config directory for global skills. Default: ~/.sharp</param>
/// <param name="SkillPaths">Explicit skill paths (files or directories)</param>
/// <param name="IncludeDefaults">Include default skills directories. Default: true</param>
public sealed record SkillLoadOptions(
    string? Cwd = null,
    string? AgentDir = null,
    IReadOnlyList<string>? SkillPaths = null,
    bool IncludeDefaults = true);

/// <summary>
/// Result of loading skills.
/// </summary>
/// <param name="Skills">Successfully loaded skills</param>
/// <param name="Diagnostics">Warnings and collision information</param>
public sealed record SkillLoadResult(
    IReadOnlyList<Skill> Skills,
    IReadOnlyList<SkillDiagnostic> Diagnostics);

/// <summary>
/// Loads skills from configured directories and explicit paths.
/// </summary>
public sealed partial class SkillLoader
{
    private const int MaxNameLength = 64;
    private const int MaxDescriptionLength = 1024;
    private const string SkillFileName = "SKILL.md";
    private const string ConfigDirName = ".sharp";

    /// <summary>
    /// Load skills from all configured locations.
    /// </summary>
    public SkillLoadResult Load(SkillLoadOptions? options = null)
    {
        options ??= new SkillLoadOptions();

        var cwd = options.Cwd ?? Directory.GetCurrentDirectory();
        var agentDir = options.AgentDir ?? GetDefaultAgentDir();
        var skillPaths = options.SkillPaths ?? [];

        var skillMap = new Dictionary<string, Skill>(StringComparer.Ordinal);
        var realPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allDiagnostics = new List<SkillDiagnostic>();
        var collisionDiagnostics = new List<SkillDiagnostic>();

        void AddSkills(SkillLoadResult result)
        {
            allDiagnostics.AddRange(result.Diagnostics);

            foreach (var skill in result.Skills)
            {
                string realPath;
                try
                {
                    realPath = Path.GetFullPath(skill.FilePath);
                }
                catch
                {
                    realPath = skill.FilePath;
                }

                // Skip if we've already loaded this exact file
                if (realPathSet.Contains(realPath))
                    continue;

                if (skillMap.TryGetValue(skill.Name, out var existing))
                {
                    collisionDiagnostics.Add(new SkillDiagnostic(
                        SkillDiagnosticType.Collision,
                        $"name \"{skill.Name}\" collision",
                        skill.FilePath,
                        new SkillCollision(skill.Name, existing.FilePath, skill.FilePath)));
                }
                else
                {
                    skillMap[skill.Name] = skill;
                    realPathSet.Add(realPath);
                }
            }
        }

        if (options.IncludeDefaults)
        {
            // User-level skills: ~/.sharp/skills
            var userSkillsDir = Path.Combine(agentDir, "skills");
            AddSkills(LoadFromDirectoryInternal(userSkillsDir, "user", includeRootFiles: true));

            // Project-level skills: .sharp/skills
            var projectSkillsDir = Path.Combine(cwd, ConfigDirName, "skills");
            AddSkills(LoadFromDirectoryInternal(projectSkillsDir, "project", includeRootFiles: true));
        }

        // Explicit skill paths
        foreach (var rawPath in skillPaths)
        {
            var resolvedPath = ResolvePath(rawPath, cwd);
            if (!Path.Exists(resolvedPath))
            {
                allDiagnostics.Add(new SkillDiagnostic(
                    SkillDiagnosticType.Warning,
                    "skill path does not exist",
                    resolvedPath));
                continue;
            }

            try
            {
                if (Directory.Exists(resolvedPath))
                {
                    AddSkills(LoadFromDirectoryInternal(resolvedPath, "path", includeRootFiles: true));
                }
                else if (File.Exists(resolvedPath) && resolvedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    var (skill, diagnostics) = LoadFromFile(resolvedPath, "path");
                    if (skill != null)
                    {
                        AddSkills(new SkillLoadResult([skill], diagnostics));
                    }
                    else
                    {
                        allDiagnostics.AddRange(diagnostics);
                    }
                }
                else
                {
                    allDiagnostics.Add(new SkillDiagnostic(
                        SkillDiagnosticType.Warning,
                        "skill path is not a markdown file",
                        resolvedPath));
                }
            }
            catch (Exception ex)
            {
                allDiagnostics.Add(new SkillDiagnostic(
                    SkillDiagnosticType.Warning,
                    ex.Message,
                    resolvedPath));
            }
        }

        allDiagnostics.AddRange(collisionDiagnostics);
        return new SkillLoadResult(skillMap.Values.ToList(), allDiagnostics);
    }

    /// <summary>
    /// Load skills from a specific directory.
    /// </summary>
    public SkillLoadResult LoadFromDirectory(string dir, string source)
        => LoadFromDirectoryInternal(dir, source, includeRootFiles: true);

    private SkillLoadResult LoadFromDirectoryInternal(string dir, string source, bool includeRootFiles)
    {
        var skills = new List<Skill>();
        var diagnostics = new List<SkillDiagnostic>();

        if (!Directory.Exists(dir))
            return new SkillLoadResult(skills, diagnostics);

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                var name = Path.GetFileName(entry);

                // Skip hidden entries and node_modules
                if (name.StartsWith('.') || name == "node_modules")
                    continue;

                if (Directory.Exists(entry))
                {
                    // Recurse into subdirectories, looking for SKILL.md
                    var subResult = LoadFromDirectoryInternal(entry, source, includeRootFiles: false);
                    skills.AddRange(subResult.Skills);
                    diagnostics.AddRange(subResult.Diagnostics);
                }
                else if (File.Exists(entry))
                {
                    var isRootMd = includeRootFiles && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
                    var isSkillMd = !includeRootFiles && name.Equals(SkillFileName, StringComparison.OrdinalIgnoreCase);

                    if (!isRootMd && !isSkillMd)
                        continue;

                    var (skill, fileDiagnostics) = LoadFromFile(entry, source);
                    if (skill != null)
                        skills.Add(skill);
                    diagnostics.AddRange(fileDiagnostics);
                }
            }
        }
        catch
        {
            // Silently ignore directory access errors
        }

        return new SkillLoadResult(skills, diagnostics);
    }

    private (Skill? Skill, List<SkillDiagnostic> Diagnostics) LoadFromFile(string filePath, string source)
    {
        var diagnostics = new List<SkillDiagnostic>();

        try
        {
            var content = File.ReadAllText(filePath);
            var (frontmatter, _) = SkillFrontmatter.Parse(content);

            var skillDir = Path.GetDirectoryName(filePath) ?? ".";
            var parentDirName = Path.GetFileName(skillDir);

            // Validate description
            var descErrors = ValidateDescription(frontmatter?.Description);
            foreach (var error in descErrors)
            {
                diagnostics.Add(new SkillDiagnostic(SkillDiagnosticType.Warning, error, filePath));
            }

            // Use name from frontmatter, or fall back to parent directory name
            var name = frontmatter?.Name ?? parentDirName;

            // Validate name
            var nameErrors = ValidateName(name, parentDirName);
            foreach (var error in nameErrors)
            {
                diagnostics.Add(new SkillDiagnostic(SkillDiagnosticType.Warning, error, filePath));
            }

            // Require description
            if (string.IsNullOrWhiteSpace(frontmatter?.Description))
            {
                return (null, diagnostics);
            }

            return (new Skill(
                Name: name,
                Description: frontmatter.Description,
                FilePath: Path.GetFullPath(filePath),
                BaseDir: Path.GetFullPath(skillDir),
                Source: source,
                DisableModelInvocation: frontmatter.DisableModelInvocation), diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new SkillDiagnostic(
                SkillDiagnosticType.Warning,
                ex.Message,
                filePath));
            return (null, diagnostics);
        }
    }

    private static List<string> ValidateName(string name, string parentDirName)
    {
        var errors = new List<string>();

        if (!name.Equals(parentDirName, StringComparison.Ordinal))
        {
            errors.Add($"name \"{name}\" does not match parent directory \"{parentDirName}\"");
        }

        if (name.Length > MaxNameLength)
        {
            errors.Add($"name exceeds {MaxNameLength} characters ({name.Length})");
        }

        if (!NamePattern().IsMatch(name))
        {
            errors.Add("name contains invalid characters (must be lowercase a-z, 0-9, hyphens only)");
        }

        if (name.StartsWith('-') || name.EndsWith('-'))
        {
            errors.Add("name must not start or end with a hyphen");
        }

        if (name.Contains("--"))
        {
            errors.Add("name must not contain consecutive hyphens");
        }

        return errors;
    }

    private static List<string> ValidateDescription(string? description)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(description))
        {
            errors.Add("description is required");
        }
        else if (description.Length > MaxDescriptionLength)
        {
            errors.Add($"description exceeds {MaxDescriptionLength} characters ({description.Length})");
        }

        return errors;
    }

    private static string ResolvePath(string input, string cwd)
    {
        var trimmed = input.Trim();

        // Handle ~ home directory
        if (trimmed == "~")
            return GetHomeDirectory();
        if (trimmed.StartsWith("~/"))
            return Path.Combine(GetHomeDirectory(), trimmed[2..]);

        return Path.IsPathRooted(trimmed)
            ? trimmed
            : Path.GetFullPath(Path.Combine(cwd, trimmed));
    }

    private static string GetHomeDirectory()
        => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string GetDefaultAgentDir()
        => Path.Combine(GetHomeDirectory(), ".sharp");

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex NamePattern();
}
