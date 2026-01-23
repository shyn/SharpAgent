using System.Text.Json;
using SharpAgent.Core.Tools;

namespace SharpAgent.Core.Tests;

public class GlobToolTests
{
    private readonly GlobTool _tool = new();

    [Fact]
    public async Task Execute_WithPattern_ReturnsMatchingFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "file1.txt"), "hello");
        File.WriteAllText(Path.Combine(tempDir, "file2.txt"), "world");
        File.WriteAllText(Path.Combine(tempDir, "file3.cs"), "code");

        try
        {
            var result = await _tool.ExecuteAsync($"{{\"pattern\": \"*.txt\", \"path\": \"{tempDir.Replace("\\", "\\\\")}\"}}");
            using var doc = JsonDocument.Parse(result.Output);
            var root = doc.RootElement;

            Assert.Equal(2, root.GetProperty("matchCount").GetInt32());
            var files = root.GetProperty("files").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains("file1.txt", files);
            Assert.Contains("file2.txt", files);
            Assert.DoesNotContain("file3.cs", files);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_WithRecursivePattern_FindsNestedFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, "subdir"));
        File.WriteAllText(Path.Combine(tempDir, "root.cs"), "root");
        File.WriteAllText(Path.Combine(tempDir, "subdir", "nested.cs"), "nested");

        try
        {
            var result = await _tool.ExecuteAsync($"{{\"pattern\": \"**/*.cs\", \"path\": \"{tempDir.Replace("\\", "\\\\")}\"}}");
            using var doc = JsonDocument.Parse(result.Output);
            var root = doc.RootElement;

            Assert.Equal(2, root.GetProperty("matchCount").GetInt32());
            var files = root.GetProperty("files").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains("root.cs", files);
            Assert.Contains("subdir/nested.cs", files);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_NoMatches_ReturnsEmptyList()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "file.txt"), "hello");

        try
        {
            var result = await _tool.ExecuteAsync($"{{\"pattern\": \"*.xyz\", \"path\": \"{tempDir.Replace("\\", "\\\\")}\"}}");
            using var doc = JsonDocument.Parse(result.Output);
            var root = doc.RootElement;

            Assert.Equal(0, root.GetProperty("matchCount").GetInt32());
            Assert.Empty(root.GetProperty("files").EnumerateArray());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_NonExistentPath_ReturnsError()
    {
        var result = await _tool.ExecuteAsync("{\"pattern\": \"*.txt\", \"path\": \"nonexistent_dir_12345\"}");
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Execute_MissingPattern_ReturnsError()
    {
        var result = await _tool.ExecuteAsync("{}");
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Execute_PatternOnlyAsString_Works()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var originalDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "test.txt"), "hello");

        try
        {
            var result = await _tool.ExecuteAsync("*.txt");
            using var doc = JsonDocument.Parse(result.Output);
            var root = doc.RootElement;

            Assert.Equal(1, root.GetProperty("matchCount").GetInt32());
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
