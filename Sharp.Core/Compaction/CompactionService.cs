using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sharp.AI;
using Sharp.Core.Sessions;

namespace Sharp.Core.Compaction;

/// <summary>
/// Service responsible for compacting conversation history when it grows too large.
/// Ported from pi-mono/packages/coding-agent/src/core/compaction/compaction.ts
/// </summary>
public sealed class CompactionService
{
    private readonly ILlmProvider _provider;
    private readonly CompactionSettings _settings;
    private readonly ILogger<CompactionService>? _logger;

    public CompactionService(
        ILlmProvider provider,
        CompactionSettings? settings = null,
        ILogger<CompactionService>? logger = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _settings = settings ?? new CompactionSettings();
        _logger = logger;
    }

    /// <summary>
    /// Gets the current compaction settings.
    /// </summary>
    public CompactionSettings Settings => _settings;

    /// <summary>
    /// Determines whether compaction should be performed based on token count.
    /// </summary>
    /// <param name="tokenCount">Current number of tokens in the conversation.</param>
    /// <param name="contextWindow">The model's context window size.</param>
    /// <returns>True if compaction should be triggered.</returns>
    public bool ShouldCompact(int tokenCount, int? contextWindow)
    {
        if (tokenCount < _settings.MinTokensForCompaction)
        {
            _logger?.LogDebug(
                "Compaction not needed: token count {TokenCount} below minimum {MinTokens}",
                tokenCount, _settings.MinTokensForCompaction);
            return false;
        }

        if (!contextWindow.HasValue)
        {
            _logger?.LogDebug("Compaction check: no context window specified");
            return false;
        }

        var effectiveLimit = (int)(contextWindow.Value * _settings.ThresholdRatio);
        var shouldCompact = tokenCount > effectiveLimit;

        _logger?.LogDebug(
            "Compaction check: {TokenCount} tokens, limit {EffectiveLimit} ({ThresholdRatio:P0} of {ContextWindow}), should compact: {ShouldCompact}",
            tokenCount, effectiveLimit, _settings.ThresholdRatio, contextWindow, shouldCompact);

        return shouldCompact;
    }

    /// <summary>
    /// Calculates the target token count after compaction.
    /// </summary>
    /// <param name="contextWindow">The model's context window size.</param>
    /// <returns>The target token count after compaction.</returns>
    public int CalculateTargetTokens(int? contextWindow)
    {
        if (!contextWindow.HasValue)
            return _settings.KeepRecentTokens;

        var availableSpace = contextWindow.Value - _settings.ReserveTokens;
        return Math.Min(availableSpace, contextWindow.Value - _settings.KeepRecentTokens);
    }

    /// <summary>
    /// Performs compaction on a conversation by summarizing older messages.
    /// </summary>
    /// <param name="entries">The session entries representing the conversation history.</param>
    /// <param name="model">The model descriptor for context window information.</param>
    /// <param name="systemPrompt">The system prompt to include in context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The compaction result, or null if compaction was not performed.</returns>
    public async Task<CompactionResult?> CompactAsync(
        IReadOnlyList<SessionEntryEnvelope> entries,
        ModelDescriptor model,
        string? systemPrompt = null,
        CancellationToken ct = default)
    {
        if (entries.Count == 0)
        {
            _logger?.LogDebug("No entries to compact");
            return null;
        }

        // Build conversation from entries and preserve entry index mapping
        var conversationEntries = BuildConversationEntries(entries);
        var conversation = conversationEntries.ConvertAll(e => e.Message);
        var tokenCount = TokenEstimator.EstimateConversationTokens(conversation, systemPrompt);

        if (!ShouldCompact(tokenCount, model.ContextWindow))
        {
            _logger?.LogDebug("Compaction not needed for {TokenCount} tokens", tokenCount);
            return null;
        }

        // Find the cut point where we should start preserving messages
        var targetTokens = CalculateTargetTokens(model.ContextWindow);
        var cutPoint = FindCutPoint(conversation, targetTokens);

        if (cutPoint <= 0)
        {
            _logger?.LogDebug("No suitable cut point found for compaction");
            return null;
        }

        var entryCutPoint = MapMessageCutPointToEntryCutPoint(conversationEntries, cutPoint, entries.Count);

        // Get the entries that will be compacted
        List<SessionEntryEnvelope> compactedEntries;
        List<SessionEntryEnvelope> keptEntries;

        if (entries is List<SessionEntryEnvelope> entriesList)
        {
            compactedEntries = entriesList.GetRange(0, entryCutPoint);
            keptEntries = entriesList.GetRange(entryCutPoint, entries.Count - entryCutPoint);
        }
        else
        {
            compactedEntries = new List<SessionEntryEnvelope>(entryCutPoint);
            for (var i = 0; i < entryCutPoint; i++)
                compactedEntries.Add(entries[i]);

            keptEntries = new List<SessionEntryEnvelope>(entries.Count - entryCutPoint);
            for (var i = entryCutPoint; i < entries.Count; i++)
                keptEntries.Add(entries[i]);
        }

        if (compactedEntries.Count == 0)
        {
            _logger?.LogDebug("No entries selected for compaction");
            return null;
        }

        // Generate summary of compacted entries
        var summary = await GenerateSummaryAsync(compactedEntries, conversation.GetRange(0, cutPoint), model, systemPrompt, ct);

        // Map back to entry IDs
        var compactedEntryIds = compactedEntries.ConvertAll(e => e.Id);
        var firstKeptEntryId = keptEntries.FirstOrDefault()?.Id;

        var tokensAfter = TokenEstimator.EstimateTokens(summary) +
                         TokenEstimator.EstimateConversationTokens(
                             conversation.Skip(cutPoint), systemPrompt);

        _logger?.LogInformation(
            "Compacted {CompactedCount} entries into summary, saved ~{TokensSaved} tokens",
            compactedEntries.Count, tokenCount - tokensAfter);

        return new CompactionResult(
            Summary: summary,
            FirstKeptEntryId: firstKeptEntryId,
            TokensBefore: tokenCount,
            TokensAfter: tokensAfter,
            CompactedEntryIds: compactedEntryIds);
    }

    /// <summary>
    /// Performs compaction with extension-provided details.
    /// </summary>
    /// <param name="entries">The session entries.</param>
    /// <param name="model">The model descriptor.</param>
    /// <param name="summary">Pre-generated summary (from extension hook).</param>
    /// <param name="firstKeptEntryId">The first entry to keep.</param>
    /// <param name="tokensBefore">Token count before compaction.</param>
    /// <param name="details">Optional extension details.</param>
    /// <param name="fromHook">Whether this was triggered by a hook.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The compaction result.</returns>
    public Task<CompactionResult> CompactWithSummaryAsync(
        IReadOnlyList<SessionEntryEnvelope> entries,
        ModelDescriptor model,
        string summary,
        string? firstKeptEntryId,
        int tokensBefore,
        JsonElement? details = null,
        bool fromHook = false,
        CancellationToken ct = default)
    {
        // Find which entries were compacted
        var compactedEntryIds = new List<string>();
        if (firstKeptEntryId != null)
        {
            foreach (var entry in entries)
            {
                if (entry.Id == firstKeptEntryId)
                    break;
                compactedEntryIds.Add(entry.Id);
            }
        }

        var tokensAfter = TokenEstimator.EstimateTokens(summary);
        if (firstKeptEntryId != null)
        {
            var keptCount = entries.Count - compactedEntryIds.Count;
            var keptEntries = entries is List<SessionEntryEnvelope> list
                ? list.GetRange(compactedEntryIds.Count, keptCount)
                : entries.Skip(compactedEntryIds.Count).ToList();
            var conversation = BuildConversation(keptEntries);
            tokensAfter += TokenEstimator.EstimateConversationTokens(conversation, null);
        }

        _logger?.LogInformation(
            "Applied external compaction summary for {CompactedCount} entries",
            compactedEntryIds.Count);

        return Task.FromResult(new CompactionResult(
            Summary: summary,
            FirstKeptEntryId: firstKeptEntryId,
            TokensBefore: tokensBefore,
            TokensAfter: tokensAfter,
            CompactedEntryIds: compactedEntryIds,
            Details: details,
            FromHook: fromHook));
    }

    private static int MapMessageCutPointToEntryCutPoint(
        IReadOnlyList<ConversationEntry> conversationEntries,
        int messageCutPoint,
        int totalEntryCount)
    {
        if (messageCutPoint <= 0)
            return 0;

        if (messageCutPoint >= conversationEntries.Count)
            return totalEntryCount;

        return conversationEntries[messageCutPoint].EntryIndex;
    }

    /// <summary>
    /// Builds a list of LLM messages from session entries.
    /// </summary>
    private static List<LlmMessage> BuildConversation(IReadOnlyList<SessionEntryEnvelope> entries)
        => BuildConversationEntries(entries).ConvertAll(e => e.Message);

    private static List<ConversationEntry> BuildConversationEntries(IReadOnlyList<SessionEntryEnvelope> entries)
    {
        var messages = new List<ConversationEntry>();

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            switch (entry.Type)
            {
                case "message":
                    var messagePayload = entry.Payload.Deserialize<MessageEntryPayload>(JsonDefaults.Options);
                    if (messagePayload?.Message != null)
                        messages.Add(new ConversationEntry(i, messagePayload.Message));
                    break;

                case "custom_message":
                    var customPayload = entry.Payload.Deserialize<CustomMessageEntryPayload>(JsonDefaults.Options);
                    if (!string.IsNullOrWhiteSpace(customPayload?.Content))
                        messages.Add(new ConversationEntry(i, LlmMessage.UserText(customPayload.Content)));
                    break;
            }
        }

        return messages;
    }

    private readonly record struct ConversationEntry(int EntryIndex, LlmMessage Message);

    /// <summary>
    /// Finds the index at which to cut the conversation for compaction.
    /// Preserves at least KeepRecentTokens from the end.
    /// </summary>
    private int FindCutPoint(IReadOnlyList<LlmMessage> conversation, int targetTokens)
    {
        if (conversation.Count == 0)
            return 0;

        var totalTokens = TokenEstimator.EstimateConversationTokens(conversation, null);
        if (totalTokens <= targetTokens)
            return 0;

        // Walk backwards from the end, finding where we exceed KeepRecentTokens
        var recentTokens = 0;
        var cutIndex = conversation.Count;

        for (var i = conversation.Count - 1; i >= 0; i--)
        {
            var messageTokens = TokenEstimator.EstimateMessageTokens(conversation[i]);
            recentTokens += messageTokens;

            if (recentTokens >= _settings.KeepRecentTokens)
            {
                cutIndex = i;
                break;
            }
        }

        // Ensure we're removing enough tokens to be worthwhile
        var tokensBeforeCut = TokenEstimator.EstimateConversationTokens(conversation.Take(cutIndex), null);
        if (tokensBeforeCut < _settings.MinTokensToCompact)
        {
            _logger?.LogDebug(
                "Tokens before cut ({TokensBeforeCut}) below minimum ({MinTokensToCompact}), adjusting cut point",
                tokensBeforeCut, _settings.MinTokensToCompact);

            // Try to find an earlier cut point
            var accumulatedTokens = 0;
            for (var i = 0; i < cutIndex; i++)
            {
                accumulatedTokens += TokenEstimator.EstimateMessageTokens(conversation[i]);
                if (accumulatedTokens >= _settings.MinTokensToCompact)
                {
                    cutIndex = i + 1;
                    break;
                }
            }
        }

        return cutIndex > 0 ? cutIndex : 0;
    }

    /// <summary>
    /// Generates a summary of the conversation using the LLM.
    /// </summary>
    private async Task<string> GenerateSummaryAsync(
        IReadOnlyList<SessionEntryEnvelope> entries,
        IReadOnlyList<LlmMessage> messagesToSummarize,
        ModelDescriptor model,
        string? systemPrompt,
        CancellationToken ct)
    {
        try
        {
            var conversationText = SerializeConversation(messagesToSummarize);

            var summaryPrompt = $@"You are summarizing a conversation between a user and an AI coding assistant.
Your task is to create a concise summary that preserves:
1. Key decisions and their rationale
2. Important file operations (reads, writes, edits)
3. Current state of work (what was being worked on)
4. Any errors or issues encountered
5. Any follow-up items or TODOs

Here is the conversation to summarize:

<conversation>
{conversationText}
</conversation>

Provide a clear, structured summary that would allow someone to continue the work from where it left off.
Focus on actionable information and current state.";

            var request = new LlmRequest(
                Model: model,
                SystemPrompt: systemPrompt ?? "You are a helpful assistant that summarizes conversations concisely.",
                Messages: [LlmMessage.UserText(summaryPrompt)],
                Tools: [],
                ThinkingLevel: ThinkingLevel.Off,
                MaxOutputTokens: Math.Min(2000, model.MaxOutputTokens ?? 4000));

            var fullText = new StringBuilder();

            await foreach (var streamEvent in _provider.StreamAsync(request, ct).WithCancellation(ct))
            {
                switch (streamEvent)
                {
                    case LlmTextDeltaEvent textDelta:
                        fullText.Append(textDelta.Delta);
                        break;
                    case LlmErrorEvent errorEvent:
                        _logger?.LogError("Error generating summary: {Error}", errorEvent.Message);
                        // Fall back to simple summary
                        return GenerateSimpleSummary(entries);
                }
            }

            var summary = fullText.ToString().Trim();
            if (string.IsNullOrWhiteSpace(summary))
            {
                return GenerateSimpleSummary(entries);
            }

            return summary;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to generate summary, falling back to simple summary");
            return GenerateSimpleSummary(entries);
        }
    }

    /// <summary>
    /// Generates a simple fallback summary when LLM-based summarization fails.
    /// </summary>
    private static string GenerateSimpleSummary(IReadOnlyList<SessionEntryEnvelope> entries)
    {
        var summary = new StringBuilder();
        summary.AppendLine("Conversation history summary:");
        summary.AppendLine();

        var fileOps = FileOperationTracker.ExtractFileOperations(entries);
        if (fileOps.ReadFiles.Count > 0)
        {
            summary.AppendLine($"Files read: {string.Join(", ", fileOps.ReadFiles.Take(10))}");
            if (fileOps.ReadFiles.Count > 10)
                summary.AppendLine($"  ... and {fileOps.ReadFiles.Count - 10} more");
        }

        if (fileOps.CreatedFiles.Count > 0)
        {
            summary.AppendLine($"Files created: {string.Join(", ", fileOps.CreatedFiles)}");
        }

        if (fileOps.EditedFiles.Count > 0)
        {
            summary.AppendLine($"Files edited: {string.Join(", ", fileOps.EditedFiles)}");
        }

        if (fileOps.DeletedFiles.Count > 0)
        {
            summary.AppendLine($"Files deleted: {string.Join(", ", fileOps.DeletedFiles)}");
        }

        summary.AppendLine();
        summary.AppendLine($"Total entries in conversation: {entries.Count}");

        return summary.ToString();
    }

    /// <summary>
    /// Serializes a conversation to text for summarization.
    /// </summary>
    private static string SerializeConversation(IReadOnlyList<LlmMessage> conversation)
    {
        var builder = new StringBuilder();

        foreach (var message in conversation)
        {
            builder.AppendLine($"[{message.Role}]");
            builder.AppendLine(MessageContent.FlattenText(message.Content));
            builder.AppendLine();
        }

        return builder.ToString();
    }
}
