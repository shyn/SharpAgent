using System.Text;
using Sharp.Core;
using Sharp.Core.Tools;
using Sharp.Core.Tests.TestDoubles;

namespace Sharp.Core.Tests;

public sealed class ToolsParityTests : IDisposable
{
    private readonly string _tempDir;

    public ToolsParityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharpagent-tools-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task ReadTool_TruncatesByLineLimit_WithContinuationHint()
    {
        var lines = Enumerable.Range(1, 700).Select(i => $"line-{i}");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "big.txt"), string.Join('\n', lines));

        var tool = new ReadTool(_tempDir);
        var ctx = new ToolExecutionContext(_tempDir, "test");
        var result = await tool.ExecuteAsync(Json("""
            {"path":"big.txt"}
            """), ctx);

        Assert.False(result.IsError);
        Assert.Contains("line-1", result.ContentAsText);
        Assert.Contains("line-500", result.ContentAsText);
        Assert.DoesNotContain("line-700", result.ContentAsText);
        Assert.Contains("Use offset=501 to continue.", result.ContentAsText);
    }

    [Fact]
    public async Task ReadTool_OffsetBeyondFileLength_ReturnsError()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "small.txt"), "a\nb\nc");

        var tool = new ReadTool(_tempDir);
        var ctx = new ToolExecutionContext(_tempDir, "test");
        var result = await tool.ExecuteAsync(Json("""
            {"path":"small.txt","offset":10}
            """), ctx);

        Assert.True(result.IsError);
        Assert.Contains("out of range", result.ContentAsText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditTool_PreservesCrLf()
    {
        var path = Path.Combine(_tempDir, "bom-crlf.txt");
        await File.WriteAllTextAsync(path, "line1\r\nline2\r\nline3");

        var tool = new EditTool(_tempDir);
        var ctx = new ToolExecutionContext(_tempDir, "test");
        var result = await tool.ExecuteAsync(Json("""
            {"path":"bom-crlf.txt","oldText":"line2\nline3","newText":"L2\nL3"}
            """), ctx);

        Assert.False(result.IsError);
        Assert.True(result.Details.HasValue);

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("\r\n", content, StringComparison.Ordinal);
        Assert.Contains("L2\r\nL3", content, StringComparison.Ordinal);
    }

    [Fact(Skip = "Pending parity with coding-agent: edit should preserve UTF-8 BOM")]
    public async Task EditTool_ShouldPreserveUtf8Bom()
    {
        var path = Path.Combine(_tempDir, "bom.txt");
        await File.WriteAllBytesAsync(path, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("line1\nline2\n")]);

        var tool = new EditTool(_tempDir);
        var ctx = new ToolExecutionContext(_tempDir, "test");
        _ = await tool.ExecuteAsync(Json("""
            {"path":"bom.txt","oldText":"line2","newText":"L2"}
            """), ctx);

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.True(bytes.Length >= 3);
        Assert.Equal((byte)0xEF, bytes[0]);
        Assert.Equal((byte)0xBB, bytes[1]);
        Assert.Equal((byte)0xBF, bytes[2]);
    }

    [Fact(Skip = "Pending parity with coding-agent: ls includeHidden should include dotfiles")]
    public async Task LsTool_CanIncludeHiddenFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, ".hidden"), "x");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "visible.txt"), "y");

        var tool = new LsTool(_tempDir);
        var ctx = new ToolExecutionContext(_tempDir, "test");

        var defaultResult = await tool.ExecuteAsync(Json("""
            {}
            """), ctx);
        Assert.DoesNotContain(".hidden", defaultResult.ContentAsText);

        var includeHiddenResult = await tool.ExecuteAsync(Json("""
            {"includeHidden":true}
            """), ctx);
        Assert.Contains(".hidden", includeHiddenResult.ContentAsText);
        Assert.Contains("visible.txt", includeHiddenResult.ContentAsText);
    }

    [Fact]
    public async Task GrepTool_SupportsIgnoreCase()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "case.txt"), "Hello\nworld");

        var tool = new GrepTool(_tempDir);
        var ctx = new ToolExecutionContext(_tempDir, "test");
        var result = await tool.ExecuteAsync(Json("""
            {"path":"case.txt","pattern":"hello","ignoreCase":true}
            """), ctx);

        Assert.False(result.IsError);
        Assert.Contains("Hello", result.ContentAsText);
    }

    [Fact]
    public async Task FindTool_SupportsRecursiveGlob()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "src", "nested"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "src", "nested", "a.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "src", "nested", "b.cs"), "b");

        var tool = new FindTool(_tempDir);
        var ctx = new ToolExecutionContext(_tempDir, "test");
        var result = await tool.ExecuteAsync(Json("""
            {"path":"src","pattern":"**/*.txt"}
            """), ctx);

        Assert.False(result.IsError);
        Assert.Contains("nested/a.txt", result.ContentAsText);
        Assert.DoesNotContain("nested/b.cs", result.ContentAsText);
    }

    [Fact]
    public async Task FindTool_FixtureSkills_ReturnsNestedSkillFiles()
    {
        var root = FixturePaths.Root;
        var tool = new FindTool(root);
        var ctx = new ToolExecutionContext(root, "test");

        var result = await tool.ExecuteAsync(Json("""
            {"path":"skills","pattern":"**/SKILL.md"}
            """), ctx);

        Assert.False(result.IsError);
        Assert.Contains("valid-skill/SKILL.md", result.ContentAsText);
        Assert.Contains("nested/child-skill/SKILL.md", result.ContentAsText);
    }

    [Fact]
    public async Task FindTool_FixtureSkillsCollision_ReturnsBothCalendarSkills()
    {
        var root = FixturePaths.Root;
        var tool = new FindTool(root);
        var ctx = new ToolExecutionContext(root, "test");

        var result = await tool.ExecuteAsync(Json("""
            {"path":"skills-collision","pattern":"**/SKILL.md"}
            """), ctx);

        Assert.False(result.IsError);
        Assert.Contains("first/calendar/SKILL.md", result.ContentAsText);
        Assert.Contains("second/calendar/SKILL.md", result.ContentAsText);
    }

    [Fact]
    public async Task ReadTool_FixtureAssistantMessage_ContainsThinkingBlock()
    {
        var root = FixturePaths.Root;
        var tool = new ReadTool(root);
        var ctx = new ToolExecutionContext(root, "test");

        var result = await tool.ExecuteAsync(Json("""
            {"path":"assistant-message-with-thinking-code.json"}
            """), ctx);

        Assert.False(result.IsError);
        Assert.Contains("\"role\": \"assistant\"", result.ContentAsText);
        Assert.Contains("\"type\": \"thinking\"", result.ContentAsText);
    }

    [Fact]
    public async Task GrepTool_FixtureBeforeCompaction_FindsCompactionEntries()
    {
        var root = FixturePaths.Root;
        var tool = new GrepTool(root);
        var ctx = new ToolExecutionContext(root, "test");

        var result = await tool.ExecuteAsync(Json("""
            {"path":"before-compaction.jsonl","pattern":"\"type\":\"compaction\""}
            """), ctx);

        Assert.False(result.IsError);
        Assert.Contains("\"type\":\"compaction\"", result.ContentAsText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LsTool_FixtureEmptyDirectories_AreReportedAsEmpty()
    {
        var root = FixturePaths.Root;
        var tool = new LsTool(root);
        var ctx = new ToolExecutionContext(root, "test");

        var emptyAgent = await tool.ExecuteAsync(Json("""
            {"path":"empty-agent"}
            """), ctx);

        Assert.False(emptyAgent.IsError);
        Assert.Equal("(empty directory)", emptyAgent.ContentAsText);

        var emptyCwd = await tool.ExecuteAsync(Json("""
            {"path":"empty-cwd"}
            """), ctx);

        Assert.False(emptyCwd.IsError);
        Assert.Equal("(empty directory)", emptyCwd.ContentAsText);
    }

    [Fact(Skip = "Pending parity with coding-agent: non-zero bash exit should map to tool error")]
    public async Task BashTool_NonZeroExit_ShouldReturnError()
    {
        var tool = new BashTool(_tempDir);
        var ctx = new ToolExecutionContext(_tempDir, "test");
        var result = await tool.ExecuteAsync(Json("""
            {"command":"exit 3"}
            """), ctx);

        Assert.True(result.IsError);
    }

    [Fact(Skip = "Pending parity with coding-agent: default ls output should include dotfiles")]
    public async Task LsTool_Default_ShouldIncludeDotfiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, ".env"), "a=b");

        var tool = new LsTool(_tempDir);
        var ctx = new ToolExecutionContext(_tempDir, "test");
        var result = await tool.ExecuteAsync(Json("{}"), ctx);

        Assert.Contains(".env", result.ContentAsText);
    }

    private static System.Text.Json.JsonElement Json(string json)
        => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
}
