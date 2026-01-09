using SharpAgent.Core.Tools;

namespace SharpAgent.Core.Tests;

public class ReadFileToolTests
{
    private readonly ReadFileTool _tool = new();

    [Fact]
    public async Task Execute_ValidFile_ReturnsContents()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Hello, World!");
        try
        {
            var result = await _tool.ExecuteAsync($"{{\"path\": \"{tempFile.Replace("\\", "\\\\")}\"}}");
            Assert.Equal("Hello, World!", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Execute_NonExistentFile_ReturnsError()
    {
        var result = await _tool.ExecuteAsync("{\"path\": \"nonexistent_file_12345.txt\"}");
        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public async Task Execute_Directory_ReturnsError()
    {
        var result = await _tool.ExecuteAsync("{\"path\": \".\"}");
        Assert.StartsWith("Error:", result);
    }
}
