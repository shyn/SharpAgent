namespace Sharp.AI;

public sealed record ThinkingBudgets(
    int? Minimal = null,
    int? Low = null,
    int? Medium = null,
    int? High = null,
    int? XHigh = null)
{
    public int? Resolve(ThinkingLevel level)
        => level switch
        {
            ThinkingLevel.Minimal => Minimal,
            ThinkingLevel.Low => Low,
            ThinkingLevel.Medium => Medium,
            ThinkingLevel.High => High,
            ThinkingLevel.XHigh => XHigh,
            _ => null
        };
}
