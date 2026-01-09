using SharpAgent.Core.Tools;

namespace SharpAgent.Core.Tests;

public class CalculatorToolTests
{
    private readonly CalculatorTool _tool = new();

    [Theory]
    [InlineData("2 + 2", "4")]
    [InlineData("10 * 5", "50")]
    [InlineData("100 / 4", "25")]
    [InlineData("(2 + 3) * 4", "20")]
    public async Task Execute_ValidExpression_ReturnsResult(string input, string expected)
    {
        var result = await _tool.ExecuteAsync(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task Execute_InvalidExpression_ReturnsError()
    {
        var result = await _tool.ExecuteAsync("not a math expression");
        Assert.StartsWith("Error:", result);
    }
}
