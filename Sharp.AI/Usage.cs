namespace Sharp.AI;

public sealed record CostBreakdown(
    decimal Input,
    decimal Output,
    decimal CacheRead,
    decimal CacheWrite,
    decimal Total);

public sealed record Usage(
    int InputTokens,
    int OutputTokens,
    int CacheReadTokens,
    int CacheWriteTokens,
    CostBreakdown Cost);
