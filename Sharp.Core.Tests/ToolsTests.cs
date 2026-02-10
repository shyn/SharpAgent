using System.Text;
using Sharp.Core;
using Sharp.Core.Tools;

namespace Sharp.Core.Tests;

public sealed class ToolsTests : IDisposable
{
    private readonly string _tempDir;

    public ToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharpagent-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task WriteTool_CreatesParentDirectoriesAndWritesFile()
    {
        var ctx = new ToolExecutionContext(_tempDir, "test");
        var tool = new WriteTool(_tempDir);
        var result = await tool.ExecuteAsync(Json("""
            {"path":"a/b/c.txt","content":"hello"}
            """), ctx);

        Assert.False(result.IsError);
        var text = await File.ReadAllTextAsync(Path.Combine(_tempDir, "a/b/c.txt"));
        Assert.Equal("hello", text);
    }

    [Fact]
    public async Task ReadTool_SupportsOffsetLimitAndBinaryDetection()
    {
        var textPath = Path.Combine(_tempDir, "sample.txt");
        await File.WriteAllTextAsync(textPath, "line1\nline2\nline3\nline4");

        var ctx = new ToolExecutionContext(_tempDir, "test");
        var read = new ReadTool(_tempDir);
        var window = await read.ExecuteAsync(Json("""
            {"path":"sample.txt","offset":2,"limit":2}
            """), ctx);

        Assert.False(window.IsError);
        Assert.Contains("line2", window.ContentAsText);
        Assert.Contains("line3", window.ContentAsText);
        Assert.DoesNotContain("line1", window.ContentAsText);

        var binaryPath = Path.Combine(_tempDir, "data.bin");
        await File.WriteAllBytesAsync(binaryPath, [0, 1, 2, 3, 4]);

        var binary = await read.ExecuteAsync(Json("""
            {"path":"data.bin"}
            """), ctx);

        Assert.Contains("Binary file detected", binary.ContentAsText);
    }

    [Fact]
    public async Task EditTool_RequiresUniqueOldTextAndReturnsDiffDetails()
    {
        var filePath = Path.Combine(_tempDir, "edit.txt");
        await File.WriteAllTextAsync(filePath, "alpha\nbeta\ngamma");

        var ctx = new ToolExecutionContext(_tempDir, "test");
        var tool = new EditTool(_tempDir);
        var ok = await tool.ExecuteAsync(Json("""
            {"path":"edit.txt","oldText":"beta","newText":"BETA"}
            """), ctx);

        Assert.False(ok.IsError);
        Assert.Contains("Updated edit.txt", ok.ContentAsText);
        Assert.True(ok.Details.HasValue);

        var updated = await File.ReadAllTextAsync(filePath);
        Assert.Contains("BETA", updated);

        var duplicatePath = Path.Combine(_tempDir, "duplicate.txt");
        await File.WriteAllTextAsync(duplicatePath, "x\nx\n");

        var duplicate = await tool.ExecuteAsync(Json("""
            {"path":"duplicate.txt","oldText":"x","newText":"y"}
            """), ctx);

        Assert.True(duplicate.IsError);
        Assert.Contains("not unique", duplicate.ContentAsText);
    }

    [Fact]
    public async Task BashTool_HandlesNormalExecutionAndTimeout()
    {
        var ctx = new ToolExecutionContext(_tempDir, "test");
        var tool = new BashTool(_tempDir);

        var success = await tool.ExecuteAsync(Json("""
            {"command":"echo test"}
            """), ctx);

        Assert.False(success.IsError);
        Assert.Contains("Exit code: 0", success.ContentAsText);
        Assert.Contains("test", success.ContentAsText);

        var timeout = await tool.ExecuteAsync(Json("""
            {"command":"sleep 2","timeout":1}
            """), ctx);

        Assert.True(timeout.IsError);
        Assert.Contains("timed out", timeout.ContentAsText);
    }

    [Fact]
    public async Task WriteTool_BlocksPathOutsideWorkspaceByDefault()
    {
        var ctx = new ToolExecutionContext(_tempDir, "test");
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.txt");

        var tool = new WriteTool(_tempDir);
        var result = await tool.ExecuteAsync(Json($$"""
            {"path":"{{outside}}","content":"blocked"}
            """), ctx);

        Assert.True(result.IsError);
        Assert.Contains("outside workspace", result.ContentAsText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GrepFindLs_WorkForBasicScenarios()
    {
        var ctx = new ToolExecutionContext(_tempDir, "test");
        Directory.CreateDirectory(Path.Combine(_tempDir, "src"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "src", "a.txt"), "alpha\nbeta\nneedle");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "src", "b.cs"), "class B {}");

        var ls = new LsTool(_tempDir);
        var lsResult = await ls.ExecuteAsync(Json("""
            {"path":"src"}
            """), ctx);
        Assert.False(lsResult.IsError);
        Assert.Contains("a.txt", lsResult.ContentAsText);

        var find = new FindTool(_tempDir);
        var findResult = await find.ExecuteAsync(Json("""
            {"path":"src","pattern":"*.txt"}
            """), ctx);
        Assert.False(findResult.IsError);
        Assert.Contains("a.txt", findResult.ContentAsText);
        Assert.DoesNotContain("b.cs", findResult.ContentAsText);

        var grep = new GrepTool(_tempDir);
        var grepResult = await grep.ExecuteAsync(Json("""
            {"path":"src","pattern":"needle"}
            """), ctx);
        Assert.False(grepResult.IsError);
        Assert.Contains("needle", grepResult.ContentAsText);
    }

    private static System.Text.Json.JsonElement Json(string json)
        => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
}
