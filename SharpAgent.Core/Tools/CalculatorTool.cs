using System.Data;
using System.Text.Json;

namespace SharpAgent.Core.Tools;

public sealed class CalculatorTool : ITool
{
    public string Name => "calculator";
    public string? WorkingDirectory { get; set; }
    public string Description => "Evaluates a mathematical expression. Input: a math expression like '2 + 2' or '(10 * 5) / 2'";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new { expression = new { type = "string", description = "The mathematical expression to evaluate" } },
        required = new[] { "expression" }
    };

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var expression = ParseInput(input);
            var result = new DataTable().Compute(expression, null);
            if (result is null)
                return Task.FromResult(ToolResult.Error("null result", "NULL_RESULT"));
            return Task.FromResult(ToolResult.Success(result.ToString()!));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error(ex.Message, "EXECUTION_ERROR"));
        }
    }

    private static string ParseInput(string input)
    {
        input = input.Trim();
        if (!input.StartsWith('{')) return input;
        
        using var doc = JsonDocument.Parse(input);
        if (doc.RootElement.TryGetProperty("expression", out var expr))
            return expr.GetString() ?? input;
        if (doc.RootElement.TryGetProperty("input", out var prop))
            return prop.GetString() ?? input;
        
        return input;
    }
}
