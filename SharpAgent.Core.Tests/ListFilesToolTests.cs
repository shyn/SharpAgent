using System.Text.Json;
using SharpAgent.Core.Tools;

namespace SharpAgent.Core.Tests;

public class ListFilesToolTests
{
    private readonly ListFilesTool _tool = new();

    private record FileEntry(string Name, string Type);

    [Fact]
    public async Task Execute_CurrentDirectory_ReturnsJsonArray()
    {
        var result = await _tool.ExecuteAsync("{}");
        
        var files = JsonSerializer.Deserialize<FileEntry[]>(result.Output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(files);
        Assert.NotEmpty(files);
    }

    [Fact]
    public async Task Execute_WithPath_ListsFilesInPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "test.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(tempDir, "subdir"));

        try
        {
            var result = await _tool.ExecuteAsync($"{{\"path\": \"{tempDir.Replace("\\", "\\\\")}\"}}");
            var files = JsonSerializer.Deserialize<FileEntry[]>(result.Output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(files);
            Assert.Contains(files, f => f.Name == "test.txt");
            Assert.Contains(files, f => f.Name == "subdir/");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_NonExistentPath_ReturnsError()
    {
        var result = await _tool.ExecuteAsync("{\"path\": \"nonexistent_dir_12345\"}");
        Assert.True(result.IsError);
    }
}
