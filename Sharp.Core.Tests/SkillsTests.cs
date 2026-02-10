using Sharp.Core.Skills;
using Sharp.Core.Tests.TestDoubles;

namespace Sharp.Core.Tests;

public sealed class SkillsTests
{
    [Fact]
    public void FrontmatterParser_ExtractsNameAndDescription()
    {
        var content = """
            ---
            name: test-skill
            description: A test skill for unit testing
            disable-model-invocation: true
            ---
            # Test Skill
            This is the body.
            """;

        var (data, body) = SkillFrontmatter.Parse(content);

        Assert.NotNull(data);
        Assert.Equal("test-skill", data.Name);
        Assert.Equal("A test skill for unit testing", data.Description);
        Assert.True(data.DisableModelInvocation);
        Assert.StartsWith("# Test Skill", body);
    }

    [Fact]
    public void FrontmatterParser_HandlesNoFrontmatter()
    {
        var content = "# Just a markdown file\nNo frontmatter here.";

        var (data, body) = SkillFrontmatter.Parse(content);

        Assert.Null(data);
        Assert.Equal(content, body);
    }

    [Fact]
    public void FrontmatterParser_HandlesQuotedValues()
    {
        var content = """
            ---
            name: "quoted-skill"
            description: 'Single quoted description'
            ---
            Body
            """;

        var (data, _) = SkillFrontmatter.Parse(content);

        Assert.NotNull(data);
        Assert.Equal("quoted-skill", data.Name);
        Assert.Equal("Single quoted description", data.Description);
    }

    [Fact]
    public void SkillLoader_LoadsSkillFromMdFile()
    {
        var loader = new SkillLoader();
        var result = loader.LoadFromDirectory(FixturePaths.Get("skills/valid-skill"), "test");

        Assert.Single(result.Skills);
        var skill = result.Skills[0];
        Assert.Equal("valid-skill", skill.Name);
        Assert.Equal("A valid skill for testing purposes.", skill.Description);
        Assert.Equal("test", skill.Source);
        Assert.False(skill.DisableModelInvocation);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void SkillLoader_SkipsInvalidSkillsWithoutDescription()
    {
        var loader = new SkillLoader();
        var result = loader.LoadFromDirectory(FixturePaths.Get("skills/missing-description"), "test");

        Assert.Empty(result.Skills);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("description is required"));
    }

    [Fact]
    public void SkillLoader_DetectsNameCollisions()
    {
        var loader = new SkillLoader();
        var result = loader.Load(new SkillLoadOptions(
            Cwd: FixturePaths.Root,
            IncludeDefaults: false,
            SkillPaths: [FixturePaths.Get("skills-collision/first"), FixturePaths.Get("skills-collision/second")]));

        Assert.Single(result.Skills);
        Assert.Equal("calendar", result.Skills[0].Name);
        Assert.Equal("path", result.Skills[0].Source);
        Assert.Contains(
            $"skills-collision{Path.DirectorySeparatorChar}first{Path.DirectorySeparatorChar}calendar{Path.DirectorySeparatorChar}SKILL.md",
            result.Skills[0].FilePath);
        Assert.Contains(result.Diagnostics, d =>
            d.Type == SkillDiagnosticType.Collision &&
            d.Message.Contains("calendar"));
    }

    [Fact]
    public void SkillLoader_ValidatesNameFormat()
    {
        var loader = new SkillLoader();
        var result = loader.LoadFromDirectory(FixturePaths.Get("skills/invalid-name-chars"), "test");

        Assert.Contains(result.Diagnostics, d =>
            d.Message.Contains("invalid characters"));
    }

    [Fact]
    public void SkillLoader_ValidatesNameMismatchFromFixture()
    {
        var loader = new SkillLoader();
        var result = loader.LoadFromDirectory(FixturePaths.Get("skills/name-mismatch"), "test");

        Assert.Single(result.Skills);
        Assert.Equal("different-name", result.Skills[0].Name);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("does not match parent directory"));
    }

    [Fact]
    public void SkillLoader_ValidatesLongNameFromFixture()
    {
        var loader = new SkillLoader();
        var result = loader.LoadFromDirectory(FixturePaths.Get("skills/long-name"), "test");

        Assert.Single(result.Skills);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("exceeds 64 characters"));
    }

    [Fact]
    public void SkillLoader_LoadsNestedSkillFromFixture()
    {
        var loader = new SkillLoader();
        var result = loader.LoadFromDirectory(FixturePaths.Get("skills/nested"), "test");

        Assert.Single(result.Skills);
        Assert.Equal("child-skill", result.Skills[0].Name);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void SkillLoader_SkipsFileWithoutFrontmatterFromFixture()
    {
        var loader = new SkillLoader();
        var result = loader.LoadFromDirectory(FixturePaths.Get("skills/no-frontmatter"), "test");

        Assert.Empty(result.Skills);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("description is required"));
    }

    [Fact]
    public void SkillLoader_WarnsOnConsecutiveHyphensFromFixture()
    {
        var loader = new SkillLoader();
        var result = loader.LoadFromDirectory(FixturePaths.Get("skills/consecutive-hyphens"), "test");

        Assert.Single(result.Skills);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("consecutive hyphens"));
    }

    [Fact]
    public void SkillLoader_LoadsDisableModelInvocationFromFixture()
    {
        var loader = new SkillLoader();
        var result = loader.LoadFromDirectory(FixturePaths.Get("skills/disable-model-invocation"), "test");

        Assert.Single(result.Skills);
        Assert.Equal("disable-model-invocation", result.Skills[0].Name);
        Assert.True(result.Skills[0].DisableModelInvocation);
    }

    [Fact]
    public void SkillLoader_AcceptsUnknownFrontmatterFieldsFromFixture()
    {
        var loader = new SkillLoader();
        var result = loader.LoadFromDirectory(FixturePaths.Get("skills/unknown-field"), "test");

        Assert.Single(result.Skills);
        Assert.Equal("unknown-field", result.Skills[0].Name);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void SkillLoader_InvalidYamlFixture_IsHandled()
    {
        var loader = new SkillLoader();
        var result = loader.LoadFromDirectory(FixturePaths.Get("skills/invalid-yaml"), "test");

        Assert.Single(result.Skills);
        Assert.Equal("invalid-yaml", result.Skills[0].Name);
        Assert.False(string.IsNullOrWhiteSpace(result.Skills[0].Description));
    }

    [Fact]
    public void SkillLoader_MultilineDescriptionFixture_IsHandled()
    {
        var loader = new SkillLoader();
        var result = loader.LoadFromDirectory(FixturePaths.Get("skills/multiline-description"), "test");

        Assert.Single(result.Skills);
        Assert.Equal("multiline-description", result.Skills[0].Name);
        Assert.False(string.IsNullOrWhiteSpace(result.Skills[0].Description));
    }

    [Fact]
    public void SkillLoader_LoadsAllSkillsFromFixtureDirectory()
    {
        var loader = new SkillLoader();
        var result = loader.LoadFromDirectory(FixturePaths.Get("skills"), "test");

        Assert.True(result.Skills.Count >= 6);
    }

    [Fact]
    public void SkillLoader_ReturnsEmptyForNonExistentDirectory()
    {
        var loader = new SkillLoader();
        var result = loader.LoadFromDirectory("/non/existent/path", "test");

        Assert.Empty(result.Skills);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void SkillLoader_Load_WithExplicitSkillPathFromFixture()
    {
        var loader = new SkillLoader();
        var result = loader.Load(new SkillLoadOptions(
            Cwd: FixturePaths.Get("empty-cwd"),
            AgentDir: FixturePaths.Get("empty-agent"),
            SkillPaths: [FixturePaths.Get("skills/valid-skill")]));

        Assert.Single(result.Skills);
        Assert.Equal("valid-skill", result.Skills[0].Name);
        Assert.Equal("path", result.Skills[0].Source);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void SkillLoader_Load_WarnsWhenSkillPathDoesNotExist()
    {
        var loader = new SkillLoader();
        var result = loader.Load(new SkillLoadOptions(
            Cwd: FixturePaths.Get("empty-cwd"),
            AgentDir: FixturePaths.Get("empty-agent"),
            SkillPaths: ["/non/existent/path"]));

        Assert.Empty(result.Skills);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("does not exist"));
    }

    [Fact]
    public void SkillLoader_Load_ExpandsTildeSkillPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var absolutePath = Path.Combine(home, ".sharp", "skills");
        var loader = new SkillLoader();

        var withTilde = loader.Load(new SkillLoadOptions(
            Cwd: FixturePaths.Get("empty-cwd"),
            AgentDir: FixturePaths.Get("empty-agent"),
            IncludeDefaults: false,
            SkillPaths: ["~/.sharp/skills"]));

        var withoutTilde = loader.Load(new SkillLoadOptions(
            Cwd: FixturePaths.Get("empty-cwd"),
            AgentDir: FixturePaths.Get("empty-agent"),
            IncludeDefaults: false,
            SkillPaths: [absolutePath]));

        Assert.Equal(withoutTilde.Skills.Count, withTilde.Skills.Count);
        Assert.Equal(withoutTilde.Diagnostics.Count, withTilde.Diagnostics.Count);
    }

    [Fact]
    public void SkillPromptFormatter_GeneratesValidXml()
    {
        var skills = new[]
        {
            new Skill("skill-one", "First skill description", "/path/to/skill-one/SKILL.md", "/path/to/skill-one", "user", false),
            new Skill("skill-two", "Second skill description", "/path/to/skill-two/SKILL.md", "/path/to/skill-two", "project", false),
            new Skill("hidden-skill", "Hidden from LLM", "/path/to/hidden/SKILL.md", "/path/to/hidden", "user", true)
        };

        var formatted = SkillPromptFormatter.FormatForPrompt(skills);

        Assert.Contains("<available_skills>", formatted);
        Assert.Contains("<name>skill-one</name>", formatted);
        Assert.Contains("<name>skill-two</name>", formatted);
        Assert.Contains("<location>/path/to/skill-one/SKILL.md</location>", formatted);
        Assert.DoesNotContain("hidden-skill", formatted);
        Assert.Contains("</available_skills>", formatted);
    }

    [Fact]
    public void SkillPromptFormatter_ReturnsEmptyForNoSkills()
    {
        var formatted = SkillPromptFormatter.FormatForPrompt([]);

        Assert.Empty(formatted);
    }

    [Fact]
    public void SkillPromptFormatter_IncludesIntroTextBeforeXml()
    {
        var skills = new[]
        {
            new Skill("test-skill", "A test skill.", "/path/to/skill/SKILL.md", "/path/to/skill", "test", false)
        };

        var formatted = SkillPromptFormatter.FormatForPrompt(skills);
        var xmlStart = formatted.IndexOf("<available_skills>", StringComparison.Ordinal);
        var intro = xmlStart >= 0 ? formatted[..xmlStart] : string.Empty;

        Assert.Contains("The following skills provide specialized instructions", intro);
        Assert.Contains("Use the read tool to load a skill's file", intro);
    }

    [Fact]
    public void SkillPromptFormatter_FormatsMultipleSkills()
    {
        var skills = new[]
        {
            new Skill("skill-one", "First skill.", "/path/one/SKILL.md", "/path/one", "test", false),
            new Skill("skill-two", "Second skill.", "/path/two/SKILL.md", "/path/two", "test", false)
        };

        var formatted = SkillPromptFormatter.FormatForPrompt(skills);
        var skillTagCount = formatted.Split("<skill>", StringSplitOptions.None).Length - 1;

        Assert.Contains("<name>skill-one</name>", formatted);
        Assert.Contains("<name>skill-two</name>", formatted);
        Assert.Equal(2, skillTagCount);
    }

    [Fact]
    public void SkillPromptFormatter_EscapesXmlCharacters()
    {
        var skills = new[]
        {
            new Skill("test", "Description with <special> & \"chars\"", "/path/SKILL.md", "/path", "user", false)
        };

        var formatted = SkillPromptFormatter.FormatForPrompt(skills);

        Assert.Contains("&lt;special&gt;", formatted);
        Assert.Contains("&amp;", formatted);
        Assert.Contains("&quot;", formatted);
    }

    [Fact]
    public void SkillPromptFormatter_ReturnsEmptyForNoVisibleSkills()
    {
        var skills = new[]
        {
            new Skill("hidden", "Hidden skill", "/path/SKILL.md", "/path", "user", true)
        };

        var formatted = SkillPromptFormatter.FormatForPrompt(skills);

        Assert.Empty(formatted);
    }
}
