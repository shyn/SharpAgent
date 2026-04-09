using System.Text;
using Sharp.AI;

namespace Sharp.Core.Compaction;

/// <summary>
/// Provides token estimation for conversation history.
/// Uses a simple approximation: ~4 characters per token on average.
/// Ported from pi-mono/packages/coding-agent/src/core/compaction/compaction.ts
/// </summary>
public static class TokenEstimator
{
    /// <summary>
    /// Approximate number of characters per token for estimation purposes.
    /// This is a rough approximation that works for English text.
    /// </summary>
    public const double CharactersPerToken = 4.0;

    /// <summary>
    /// Base token count for message overhead (role markers, formatting).
    /// </summary>
    public const int MessageOverheadTokens = 4;

    /// <summary>
    /// Estimates the number of tokens in a string.
    /// </summary>
    /// <param name="text">The text to estimate.</param>
    /// <returns>The estimated token count.</returns>
    public static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Simple estimation based on character count
        // This is less accurate than tiktoken but much faster and has no dependencies
        // Equivalent to Math.Ceiling(text.Length / 4.0) but using integer arithmetic
        return (text.Length + 3) / 4;
    }

    /// <summary>
    /// Estimates the number of tokens in a message.
    /// </summary>
    /// <param name="message">The message to estimate.</param>
    /// <returns>The estimated token count.</returns>
    public static int EstimateMessageTokens(LlmMessage message)
    {
        if (message?.Content == null)
            return 0;

        var contentTokens = 0;

        foreach (var block in message.Content)
        {
            contentTokens += block switch
            {
                TextContentBlock text => EstimateTokens(text.Text),
                ThinkingContentBlock thinking => EstimateTokens(thinking.Text),
                ToolResultContentBlock toolResult => EstimateTokens(toolResult.ContentText) + EstimateTokens(toolResult.ToolName),
                ToolCallContentBlock toolCall => EstimateTokens(toolCall.ArgumentsJson) + EstimateTokens(toolCall.ToolName) + EstimateTokens(toolCall.ToolCallId),
                ImageContentBlock => 1000, // Rough estimate for image tokens
                _ => 0
            };
        }

        return MessageOverheadTokens + contentTokens;
    }

    /// <summary>
    /// Estimates the total token count for a conversation.
    /// </summary>
    /// <param name="conversation">The list of messages.</param>
    /// <param name="systemPrompt">Optional system prompt to include.</param>
    /// <returns>The estimated total token count.</returns>
    public static int EstimateConversationTokens(IEnumerable<LlmMessage> conversation, string? systemPrompt)
    {
        var total = 0;

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            total += MessageOverheadTokens + EstimateTokens(systemPrompt);
        }

        foreach (var message in conversation)
        {
            total += EstimateMessageTokens(message);
        }

        return total;
    }

    /// <summary>
    /// Estimates the remaining available tokens in the context window.
    /// </summary>
    /// <param name="conversation">The current conversation.</param>
    /// <param name="systemPrompt">The system prompt.</param>
    /// <param name="contextWindow">The model's context window size.</param>
    /// <returns>The number of available tokens, or null if context window is unknown.</returns>
    public static int? EstimateAvailableTokens(IReadOnlyList<LlmMessage> conversation, string? systemPrompt, int? contextWindow)
    {
        if (!contextWindow.HasValue)
            return null;

        var used = EstimateConversationTokens(conversation, systemPrompt);
        return Math.Max(0, contextWindow.Value - used);
    }

    /// <summary>
    /// Checks if the conversation is approaching the context window limit.
    /// </summary>
    /// <param name="conversation">The current conversation.</param>
    /// <param name="systemPrompt">The system prompt.</param>
    /// <param name="contextWindow">The model's context window size.</param>
    /// <param name="thresholdRatio">The threshold ratio (0.0-1.0).</param>
    /// <returns>True if approaching the limit.</returns>
    public static bool IsApproachingLimit(IReadOnlyList<LlmMessage> conversation, string? systemPrompt, int? contextWindow, double thresholdRatio = 0.9)
    {
        if (!contextWindow.HasValue)
            return false;

        var used = EstimateConversationTokens(conversation, systemPrompt);
        return used > contextWindow.Value * thresholdRatio;
    }

    /// <summary>
    /// Calculates the cumulative token counts at each position in the conversation.
    /// Useful for finding cut points for compaction.
    /// </summary>
    /// <param name="conversation">The conversation messages.</param>
    /// <param name="systemPrompt">Optional system prompt.</param>
    /// <returns>An array of cumulative token counts.</returns>
    public static int[] CalculateCumulativeTokens(IEnumerable<LlmMessage> conversation, string? systemPrompt)
    {
        var result = new List<int>();
        var cumulative = 0;

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            cumulative += MessageOverheadTokens + EstimateTokens(systemPrompt);
        }

        foreach (var message in conversation)
        {
            cumulative += EstimateMessageTokens(message);
            result.Add(cumulative);
        }

        return result.ToArray();
    }

    /// <summary>
    /// Finds the index where the conversation exceeds a token threshold.
    /// </summary>
    /// <param name="conversation">The conversation messages.</param>
    /// <param name="systemPrompt">Optional system prompt.</param>
    /// <param name="tokenThreshold">The token threshold.</param>
    /// <returns>The index where threshold is exceeded, or -1 if never exceeded.</returns>
    public static int FindTokenThresholdIndex(IReadOnlyList<LlmMessage> conversation, string? systemPrompt, int tokenThreshold)
    {
        var cumulative = 0;

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            cumulative += MessageOverheadTokens + EstimateTokens(systemPrompt);
        }

        for (var i = 0; i < conversation.Count; i++)
        {
            cumulative += EstimateMessageTokens(conversation[i]);
            if (cumulative > tokenThreshold)
                return i;
        }

        return -1;
    }
}
