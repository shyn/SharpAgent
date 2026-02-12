namespace Sharp.Core.Configuration;

public sealed class ModelOverrideConfig
{
    public string? Name { get; set; }
    public bool? Reasoning { get; set; }
    public string[]? Input { get; set; }
    public ModelPricingConfig? Cost { get; set; }
    public int? ContextWindow { get; set; }
    public int? MaxOutputTokens { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public OpenAiCompletionsCompatConfig? Compat { get; set; }
}
