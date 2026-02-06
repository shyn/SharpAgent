using SharpAgent.Core.Tools;
using System.Text.Json;

namespace SharpAgent.Core.Tests;

public class GrepToolTests : IDisposable
{
    private readonly GrepTool _tool = new();
    private readonly string _testFile;

    public GrepToolTests()
    {
        _testFile = Path.Combine(Path.GetTempPath(), $"grep_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(_testFile, "Hello World\nSharpAgent is cool\nGrep tool implementation\nLine with 123 numbers");
    }

    public void Dispose()
    {
        if (File.Exists(_testFile))
            File.Delete(_testFile);
    }

    [Fact]
    public async Task Execute_SimpleSearch_ReturnsMatch()
    {
        var input = JsonSerializer.Serialize(new
        {
            pattern = "SharpAgent",
            path = _testFile
        });

        var result = await _tool.ExecuteAsync(input);

        Assert.Contains("SharpAgent is cool", result.Output);
        Assert.Contains(_testFile, result.Output);
    }

    [Fact]
    public async Task Execute_CaseInsensitiveSearch_ReturnsMatch()
    {
        var input = JsonSerializer.Serialize(new
        {
            pattern = "sharpagent",
            path = _testFile,
            caseInsensitive = true
        });

        var result = await _tool.ExecuteAsync(input);

        Assert.Contains("SharpAgent is cool", result.Output);
    }

    [Fact]
    public async Task Execute_RegexSearch_ReturnsMatch()
    {
        var input = JsonSerializer.Serialize(new
        {
            pattern = "L.*123",
            path = _testFile,
            isRegex = true
        });

        var result = await _tool.ExecuteAsync(input);

        Assert.Contains("Line with 123 numbers", result.Output);
    }

    [Fact]
    public async Task Execute_LiteralSearch_DoesNotMatchRegex()
    {
        var input = JsonSerializer.Serialize(new
        {
            pattern = "L.*123",
            path = _testFile,
            isRegex = false
        });

        var result = await _tool.ExecuteAsync(input);

        Assert.DoesNotContain("Line with 123 numbers", result.Output);
    }

    [Fact]
    public async Task Execute_NoMatches_ReturnsMessage()
    {
        var input = JsonSerializer.Serialize(new
        {
            pattern = "NonExistentPattern",
            path = _testFile
        });

        var result = await _tool.ExecuteAsync(input);

        Assert.Contains("No matches found", result.Output);
    }
}
