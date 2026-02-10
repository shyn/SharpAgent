using Sharp.AI;
using Sharp.Core.Resources;
using Sharp.Core.Tools;

namespace Sharp.Core.Tests;

public sealed class SessionResourceLoadingTests : IDisposable
{
    private readonly string _tempDir;

    public SessionResourceLoadingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharpagent-resource-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void SessionResourceLoader_Load_IncludesGlobalAndAncestorContextFilesInOrder()
    {
        var agentDir = Path.Combine(_tempDir, "agent");
        Directory.CreateDirectory(agentDir);
        File.WriteAllText(Path.Combine(agentDir, "AGENTS.md"), "global-agents");
        File.WriteAllText(Path.Combine(agentDir, "CLAUDE.md"), "global-claude");

        var root = Path.Combine(_tempDir, "workspace");
        var repo = Path.Combine(root, "repo");
        var leaf = Path.Combine(repo, "leaf");
        Directory.CreateDirectory(leaf);

        File.WriteAllText(Path.Combine(root, "CLAUDE.md"), "root-claude");
        File.WriteAllText(Path.Combine(repo, "AGENTS.md"), "repo-agents");
        File.WriteAllText(Path.Combine(repo, "CLAUDE.md"), "repo-claude");

        var loader = new SessionResourceLoader();
        var result = loader.Load(new SessionResourceOptions(
            WorkingDirectory: leaf,
            AgentDirectory: agentDir,
            BaseSystemPrompt: "base",
            EnableSkills: false));

        Assert.Equal(3, result.ContextFiles.Count);
        Assert.Equal(Path.GetFullPath(Path.Combine(agentDir, "AGENTS.md")), result.ContextFiles[0].Path);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "CLAUDE.md")), result.ContextFiles[1].Path);
        Assert.Equal(Path.GetFullPath(Path.Combine(repo, "AGENTS.md")), result.ContextFiles[2].Path);
    }

    [Fact]
    public void SessionResourceLoader_Load_LoadsAppendPromptAndSkills()
    {
        var cwd = Path.Combine(_tempDir, "project");
        Directory.CreateDirectory(Path.Combine(cwd, ".sharp", "skills", "test-skill"));
        File.WriteAllText(Path.Combine(cwd, ".sharp", "APPEND_SYSTEM.md"), "append-section");
        File.WriteAllText(
            Path.Combine(cwd, ".sharp", "skills", "test-skill", "SKILL.md"),
            """
            ---
            name: test-skill
            description: Skill description for testing
            ---
            # Test Skill
            Body
            """);

        var loader = new SessionResourceLoader();
        var result = loader.Load(new SessionResourceOptions(
            WorkingDirectory: cwd,
            AgentDirectory: Path.Combine(_tempDir, "agent"),
            BaseSystemPrompt: "base"));

        Assert.Single(result.AppendSystemPromptSections);
        Assert.Equal("append-section", result.AppendSystemPromptSections[0]);
        Assert.Single(result.Skills);

        var prompt = SystemPromptBuilder.Build(
            result.BaseSystemPrompt,
            result.AppendSystemPromptSections,
            result.ContextFiles,
            result.Skills,
            includeSkills: true);

        Assert.Contains("append-section", prompt, StringComparison.Ordinal);
        Assert.Contains("<available_skills>", prompt, StringComparison.Ordinal);
        Assert.Contains("<name>test-skill</name>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentSession_CreateAsync_InjectsContextAndSkillsIntoSystemPrompt()
    {
        var cwd = Path.Combine(_tempDir, "project");
        var agentDir = Path.Combine(_tempDir, "agent");
        Directory.CreateDirectory(cwd);
        Directory.CreateDirectory(agentDir);
        Directory.CreateDirectory(Path.Combine(cwd, ".sharp", "skills", "test-skill"));

        File.WriteAllText(Path.Combine(cwd, "AGENTS.md"), "project-rules");
        File.WriteAllText(Path.Combine(cwd, ".sharp", "APPEND_SYSTEM.md"), "append-rules");
        File.WriteAllText(
            Path.Combine(cwd, ".sharp", "skills", "test-skill", "SKILL.md"),
            """
            ---
            name: test-skill
            description: Skill description for testing
            ---
            # Test Skill
            Body
            """);

        var options = new AgentRuntimeOptions
        {
            Model = new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            ApiKey = "test-key",
            BaseUrl = "https://example.com/v1/",
            WorkingDirectory = cwd,
            SessionDirectory = Path.Combine(_tempDir, "sessions"),
            AgentDirectory = agentDir,
            SystemPrompt = "base-system"
        };

        using var session = await AgentSession.CreateAsync(options);

        Assert.Contains("base-system", session.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("project-rules", session.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("append-rules", session.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("<available_skills>", session.SystemPrompt, StringComparison.Ordinal);
        Assert.Single(session.ResourceSnapshot.Skills);
        Assert.Equal(session.SystemPrompt, session.ResourceSnapshot.FinalSystemPrompt);
    }

    [Fact]
    public async Task AgentSession_CreateAsync_WithoutReadTool_DoesNotInjectSkillsSection()
    {
        var cwd = Path.Combine(_tempDir, "project-no-read");
        Directory.CreateDirectory(Path.Combine(cwd, ".sharp", "skills", "test-skill"));
        File.WriteAllText(
            Path.Combine(cwd, ".sharp", "skills", "test-skill", "SKILL.md"),
            """
            ---
            name: test-skill
            description: Skill description for testing
            ---
            # Test Skill
            Body
            """);

        var options = new AgentRuntimeOptions
        {
            Model = new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
            ApiKey = "test-key",
            BaseUrl = "https://example.com/v1/",
            WorkingDirectory = cwd,
            SessionDirectory = Path.Combine(_tempDir, "sessions-no-read"),
            AgentDirectory = Path.Combine(_tempDir, "agent-no-read")
        };

        using var session = await AgentSession.CreateAsync(options, tools: [new LsTool(cwd)]);

        Assert.DoesNotContain("<available_skills>", session.SystemPrompt, StringComparison.Ordinal);
        Assert.Single(session.ResourceSnapshot.Skills);
    }

    [Fact]
    public void SessionResourceLoader_Load_DiscoversSystemPromptFileWithProjectPrecedence()
    {
        var cwd = Path.Combine(_tempDir, "project-system");
        var agentDir = Path.Combine(_tempDir, "agent-system");
        Directory.CreateDirectory(Path.Combine(cwd, ".sharp"));
        Directory.CreateDirectory(agentDir);

        File.WriteAllText(Path.Combine(agentDir, "SYSTEM.md"), "global-system");
        File.WriteAllText(Path.Combine(cwd, ".sharp", "SYSTEM.md"), "project-system");

        var loader = new SessionResourceLoader();
        var discovered = loader.Load(new SessionResourceOptions(
            WorkingDirectory: cwd,
            AgentDirectory: agentDir,
            BaseSystemPrompt: "fallback"));

        Assert.Equal("project-system", discovered.BaseSystemPrompt);

        var noDiscovery = loader.Load(new SessionResourceOptions(
            WorkingDirectory: cwd,
            AgentDirectory: agentDir,
            BaseSystemPrompt: "fallback",
            DiscoverSystemPromptFile: false));

        Assert.Equal("fallback", noDiscovery.BaseSystemPrompt);
    }

    [Fact]
    public void SessionResourceLoader_Load_AggregatesSkillDiagnosticsIntoResourceDiagnostics()
    {
        var cwd = Path.Combine(_tempDir, "project-diag");
        Directory.CreateDirectory(Path.Combine(cwd, ".sharp", "skills", "missing-description"));
        File.WriteAllText(
            Path.Combine(cwd, ".sharp", "skills", "missing-description", "SKILL.md"),
            """
            ---
            name: missing-description
            ---
            # Missing Description
            Body
            """);

        var loader = new SessionResourceLoader();
        var result = loader.Load(new SessionResourceOptions(
            WorkingDirectory: cwd,
            AgentDirectory: Path.Combine(_tempDir, "agent-diag"),
            BaseSystemPrompt: "base"));

        Assert.NotEmpty(result.SkillDiagnostics);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("description is required", StringComparison.Ordinal));
    }
}
