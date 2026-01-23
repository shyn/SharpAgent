using SharpAgent.Core.Tools;

namespace SharpAgent.Core.Tests;

public class EditFileToolTests
{
    private readonly EditFileTool _tool = new();

    [Fact]
    public async Task Execute_ReplaceText_ReturnsOk()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Hello, World!");
        try
        {
            var result = await _tool.ExecuteAsync($$"""{"path": "{{tempFile.Replace("\\", "\\\\")}}", "old_str": "World", "new_str": "Universe"}""");
            Assert.Equal("OK", result.Output);
            Assert.Equal("Hello, Universe!", File.ReadAllText(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Execute_CreateNewFile_ReturnsOk()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            var result = await _tool.ExecuteAsync($$"""{"path": "{{tempFile.Replace("\\", "\\\\")}}", "old_str": "", "new_str": "New content"}""");
            Assert.Equal("OK", result.Output);
            Assert.Equal("New content", File.ReadAllText(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Execute_OldStrNotFound_ReturnsError()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Hello, World!");
        try
        {
            var result = await _tool.ExecuteAsync($$"""{"path": "{{tempFile.Replace("\\", "\\\\")}}", "old_str": "NotHere", "new_str": "Something"}""");
            Assert.True(result.IsError);
            Assert.Contains("not found", result.Output);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Execute_SameOldAndNew_ReturnsError()
    {
        var result = await _tool.ExecuteAsync("""{"path": "test.txt", "old_str": "same", "new_str": "same"}""");
        Assert.True(result.IsError);
        Assert.Contains("different", result.Output);
    }

    [Fact]
    public async Task Execute_NonExistentFileWithOldStr_ReturnsError()
    {
        var result = await _tool.ExecuteAsync("""{"path": "nonexistent_12345.txt", "old_str": "find", "new_str": "replace"}""");
        Assert.True(result.IsError);
    }
}
