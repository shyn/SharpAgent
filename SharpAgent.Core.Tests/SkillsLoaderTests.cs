using SharpAgent.Core.Skills;

namespace SharpAgent.Core.Tests;

public class SkillsLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public SkillsLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"skills-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void IsValidSkillName_ValidNames_ReturnsTrue()
    {
        Assert.True(SkillsLoader.IsValidSkillName("pdf-processing"));
        Assert.True(SkillsLoader.IsValidSkillName("data-analysis"));
        Assert.True(SkillsLoader.IsValidSkillName("code-review"));
        Assert.True(SkillsLoader.IsValidSkillName("a"));
        Assert.True(SkillsLoader.IsValidSkillName("skill123"));
    }

    [Fact]
    public void IsValidSkillName_InvalidNames_ReturnsFalse()
    {
        Assert.False(SkillsLoader.IsValidSkillName("PDF-Processing")); // uppercase
        Assert.False(SkillsLoader.IsValidSkillName("-pdf")); // starts with hyphen
        Assert.False(SkillsLoader.IsValidSkillName("pdf-")); // ends with hyphen
        Assert.False(SkillsLoader.IsValidSkillName("pdf--processing")); // consecutive hyphens
        Assert.False(SkillsLoader.IsValidSkillName("")); // empty
        Assert.False(SkillsLoader.IsValidSkillName(new string('a', 65))); // too long
        Assert.False(SkillsLoader.IsValidSkillName("skill_name")); // underscore not allowed
    }

    [Fact]
    public void ParseSkill_ValidSkillMd_ReturnsMetadata()
    {
        var content = """
            ---
            name: pdf-processing
            description: Extracts text and tables from PDF files, fills PDF forms, and merges multiple PDFs.
            license: Apache-2.0
            ---
            # PDF Processing Skill

            This skill helps you work with PDF files.

            ## Usage

            Use this when the user asks about PDFs.
            """;

        var result = SkillsLoader.ParseSkill(content, "/path/to/SKILL.md");

        Assert.NotNull(result);
        Assert.Equal("pdf-processing", result.Name);
        Assert.Equal("Extracts text and tables from PDF files, fills PDF forms, and merges multiple PDFs.", result.Description);
        Assert.Equal("Apache-2.0", result.License);
        Assert.Contains("PDF Processing Skill", result.Body);
        Assert.Contains("Use this when the user asks about PDFs.", result.Body);
    }

    [Fact]
    public void ParseSkill_WithMetadataBlock_ParsesMetadata()
    {
        var content = """
            ---
            name: test-skill
            description: A test skill for unit testing.
            metadata:
              author: example-org
              version: "1.0"
            ---
            # Test Skill
            """;

        var result = SkillsLoader.ParseSkill(content, "/path/to/SKILL.md");

        Assert.NotNull(result);
        Assert.NotNull(result.Metadata);
        Assert.Equal("example-org", result.Metadata["author"]);
        Assert.Equal("1.0", result.Metadata["version"]);
    }

    [Fact]
    public void ParseSkill_MissingName_ReturnsNull()
    {
        var content = """
            ---
            description: A skill without a name.
            ---
            # Body
            """;

        var result = SkillsLoader.ParseSkill(content, "/path/to/SKILL.md");

        Assert.Null(result);
    }

    [Fact]
    public void ParseSkill_MissingDescription_ReturnsNull()
    {
        var content = """
            ---
            name: test-skill
            ---
            # Body
            """;

        var result = SkillsLoader.ParseSkill(content, "/path/to/SKILL.md");

        Assert.Null(result);
    }

    [Fact]
    public void ParseSkill_InvalidName_ReturnsNull()
    {
        var content = """
            ---
            name: Invalid_Name
            description: A skill with invalid name.
            ---
            # Body
            """;

        var result = SkillsLoader.ParseSkill(content, "/path/to/SKILL.md");

        Assert.Null(result);
    }

    [Fact]
    public void ParseSkill_NoFrontmatter_ReturnsNull()
    {
        var content = """
            # No Frontmatter
            This is just markdown without frontmatter.
            """;

        var result = SkillsLoader.ParseSkill(content, "/path/to/SKILL.md");

        Assert.Null(result);
    }

    [Fact]
    public async Task DiscoverAsync_FindsValidSkills()
    {
        // Create a valid skill directory
        var skillDir = Path.Combine(_tempDir, "test-skill");
        Directory.CreateDirectory(skillDir);
        await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), """
            ---
            name: test-skill
            description: A test skill for discovery testing.
            ---
            # Test Skill Instructions
            """);

        var skills = await SkillsLoader.DiscoverAsync([_tempDir]);

        Assert.Single(skills);
        Assert.Equal("test-skill", skills[0].Name);
    }

    [Fact]
    public async Task DiscoverAsync_SkipsMismatchedDirectoryName()
    {
        // Create a skill where name doesn't match directory
        var skillDir = Path.Combine(_tempDir, "wrong-dir-name");
        Directory.CreateDirectory(skillDir);
        await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), """
            ---
            name: different-name
            description: Name doesn't match directory.
            ---
            # Body
            """);

        var skills = await SkillsLoader.DiscoverAsync([_tempDir]);

        Assert.Empty(skills);
    }

    [Fact]
    public async Task DiscoverAsync_SkipsDirectoriesWithoutSkillMd()
    {
        // Create a directory without SKILL.md
        var skillDir = Path.Combine(_tempDir, "not-a-skill");
        Directory.CreateDirectory(skillDir);
        await File.WriteAllTextAsync(Path.Combine(skillDir, "README.md"), "# Not a skill");

        var skills = await SkillsLoader.DiscoverAsync([_tempDir]);

        Assert.Empty(skills);
    }

    [Fact]
    public async Task DiscoverAsync_Recursive_FindsNestedSkills()
    {
        // Create nested skill directories
        var nestedDir = Path.Combine(_tempDir, "category", "subcategory", "nested-skill");
        Directory.CreateDirectory(nestedDir);
        await File.WriteAllTextAsync(Path.Combine(nestedDir, "SKILL.md"), """
            ---
            name: nested-skill
            description: A deeply nested skill.
            ---
            # Nested Skill
            """);

        var skills = await SkillsLoader.DiscoverAsync([(_tempDir, true)]);

        Assert.Single(skills);
        Assert.Equal("nested-skill", skills[0].Name);
    }

    [Fact]
    public async Task DiscoverAsync_NonRecursive_SkipsNestedSkills()
    {
        // Create nested skill that should be skipped in non-recursive mode
        var nestedDir = Path.Combine(_tempDir, "category", "subcategory", "nested-skill");
        Directory.CreateDirectory(nestedDir);
        await File.WriteAllTextAsync(Path.Combine(nestedDir, "SKILL.md"), """
            ---
            name: nested-skill
            description: A deeply nested skill.
            ---
            # Nested Skill
            """);

        var skills = await SkillsLoader.DiscoverAsync([(_tempDir, false)]);

        Assert.Empty(skills);
    }

    [Fact]
    public async Task DiscoverAsync_DeduplicatesSkillsByName()
    {
        // Create same skill in two directories
        var dir1 = Path.Combine(_tempDir, "dir1", "my-skill");
        var dir2 = Path.Combine(_tempDir, "dir2", "my-skill");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        
        await File.WriteAllTextAsync(Path.Combine(dir1, "SKILL.md"), """
            ---
            name: my-skill
            description: First version.
            ---
            # First
            """);
        await File.WriteAllTextAsync(Path.Combine(dir2, "SKILL.md"), """
            ---
            name: my-skill
            description: Second version.
            ---
            # Second
            """);

        var skills = await SkillsLoader.DiscoverAsync([
            (Path.Combine(_tempDir, "dir1"), false),
            (Path.Combine(_tempDir, "dir2"), false)
        ]);

        Assert.Single(skills);
        Assert.Equal("First version.", skills[0].Description); // First one wins
    }

    [Fact]
    public void BuildAvailableSkillsPrompt_GeneratesValidXml()
    {
        var skills = new List<SkillMetadata>
        {
            new()
            {
                Name = "pdf-processing",
                Description = "Works with PDF files.",
                Location = "/path/to/pdf-processing/SKILL.md",
                Body = "# PDF Skill"
            },
            new()
            {
                Name = "data-analysis",
                Description = "Analyzes datasets.",
                Location = "/path/to/data-analysis/SKILL.md",
                Body = "# Data Skill"
            }
        };

        var result = SkillsLoader.BuildAvailableSkillsPrompt(skills);

        // Check skill usage instructions
        Assert.Contains("# Skills", result);
        Assert.Contains("self-contained capability packages", result);
        Assert.Contains("`read_file` tool", result);
        Assert.Contains("<location>", result);
        
        // Check available skills XML
        Assert.Contains("<available_skills>", result);
        Assert.Contains("</available_skills>", result);
        Assert.Contains("<name>pdf-processing</name>", result);
        Assert.Contains("<description>Works with PDF files.</description>", result);
        Assert.Contains("<name>data-analysis</name>", result);
    }

    [Fact]
    public void BuildAvailableSkillsPrompt_EmptyList_ReturnsEmpty()
    {
        var result = SkillsLoader.BuildAvailableSkillsPrompt([]);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildAvailableSkillsPrompt_EscapesXmlCharacters()
    {
        var skills = new List<SkillMetadata>
        {
            new()
            {
                Name = "test-skill",
                Description = "Handles <xml> & \"quotes\" safely.",
                Location = "/path/to/test-skill/SKILL.md",
                Body = "# Test"
            }
        };

        var result = SkillsLoader.BuildAvailableSkillsPrompt(skills);

        Assert.Contains("&lt;xml&gt;", result);
        Assert.Contains("&amp;", result);
        Assert.Contains("&quot;quotes&quot;", result);
    }
}
