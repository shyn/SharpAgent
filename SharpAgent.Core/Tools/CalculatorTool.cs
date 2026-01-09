using System.Data;
using System.Text.Json;

namespace SharpAgent.Core.Tools;

public sealed class CalculatorTool : ITool
{
    public string Name => "calculator";
    public string Description => "Evaluates a mathematical expression. Input: a math expression like '2 + 2' or '(10 * 5) / 2'";

    public Task<string> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var expression = ParseInput(input);
            var result = new DataTable().Compute(expression, null);
            return Task.FromResult(result?.ToString() ?? "Error: null result");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    private static string ParseInput(string input)
    {
        input = input.Trim();
        if (!input.StartsWith('{')) return input;
        
        using var doc = JsonDocument.Parse(input);
        if (doc.RootElement.TryGetProperty("input", out var prop))
            return prop.GetString() ?? input;
        
        return input;
    }
}
