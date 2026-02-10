namespace Sharp.Core.Compaction;

/// <summary>
/// Settings for controlling context compaction behavior.
/// </summary>
public sealed record CompactionSettings
{
    /// <summary>
    /// Default number of tokens to reserve for the model's output and working space.
    /// Default: 16,000 tokens.
    /// </summary>
    public int ReserveTokens { get; init; } = 16000;

    /// <summary>
    /// Number of recent tokens to preserve during compaction.
    /// When compacting, at least this many tokens from the end will be kept.
    /// Default: 20,000 tokens.
    /// </summary>
    public int KeepRecentTokens { get; init; } = 20000;

    /// <summary>
    /// Threshold ratio (0.0-1.0) of context window at which compaction should be triggered.
    /// Default: 0.9 (90% of context window).
    /// </summary>
    public double ThresholdRatio { get; init; } = 0.9;

    /// <summary>
    /// Minimum number of tokens in the conversation before compaction is considered.
    /// Default: 1000 tokens.
    /// </summary>
    public int MinTokensForCompaction { get; init; } = 1000;

    /// <summary>
    /// Minimum number of tokens that must be removed during compaction to be worthwhile.
    /// Default: 4000 tokens.
    /// </summary>
    public int MinTokensToCompact { get; init; } = 4000;

    /// <summary>
    /// Creates a copy of these settings with modified values.
    /// </summary>
    public CompactionSettings With(
        int? reserveTokens = null,
        int? keepRecentTokens = null,
        double? thresholdRatio = null,
        int? minTokensForCompaction = null,
        int? minTokensToCompact = null)
        => new()
        {
            ReserveTokens = reserveTokens ?? ReserveTokens,
            KeepRecentTokens = keepRecentTokens ?? KeepRecentTokens,
            ThresholdRatio = thresholdRatio ?? ThresholdRatio,
            MinTokensForCompaction = minTokensForCompaction ?? MinTokensForCompaction,
            MinTokensToCompact = minTokensToCompact ?? MinTokensToCompact
        };
}
