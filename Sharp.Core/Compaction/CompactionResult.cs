using System.Text.Json;

namespace Sharp.Core.Compaction;

/// <summary>
/// Represents the result of a compaction operation.
/// </summary>
/// <param name="Summary">The generated summary of compacted history.</param>
/// <param name="FirstKeptEntryId">The ID of the first entry that was kept (not compacted).</param>
/// <param name="TokensBefore">The number of tokens before compaction.</param>
/// <param name="TokensAfter">The estimated number of tokens after compaction.</param>
/// <param name="CompactedEntryIds">The IDs of entries that were compacted into the summary.</param>
/// <param name="Details">Optional extension-specific details.</param>
/// <param name="FromHook">Whether this compaction was triggered by an extension hook.</param>
public sealed record CompactionResult(
    string Summary,
    string? FirstKeptEntryId,
    int TokensBefore,
    int TokensAfter,
    IReadOnlyList<string> CompactedEntryIds,
    JsonElement? Details = null,
    bool FromHook = false)
{
    /// <summary>
    /// The number of tokens saved by compaction.
    /// </summary>
    public int TokensSaved => TokensBefore - TokensAfter;

    /// <summary>
    /// The ratio of tokens saved (0.0-1.0).
    /// </summary>
    public double SavingsRatio => TokensBefore > 0 ? (double)TokensSaved / TokensBefore : 0;
}
