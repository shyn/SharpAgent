using Sharp.AI;
using Sharp.Core.Compaction;
using Xunit;

namespace Sharp.Core.Tests.Compaction;

public class TokenEstimatorTests
{
    [Fact]
    public void EstimateTokens_EmptyOrNull_ReturnsZero()
    {
        Assert.Equal(0, TokenEstimator.EstimateTokens(null));
        Assert.Equal(0, TokenEstimator.EstimateTokens(""));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(100, 25)]
    [InlineData(101, 26)]
    public void EstimateTokens_ReturnsCorrectEstimate(int length, int expectedTokens)
    {
        var text = new string('a', length);
        Assert.Equal(expectedTokens, TokenEstimator.EstimateTokens(text));
    }

    [Fact]
    public void FindTokenThresholdIndex_EmptyConversation_ReturnsMinusOne()
    {
        var conversation = new List<LlmMessage>();
        var index = TokenEstimator.FindTokenThresholdIndex(conversation, null, 100);
        Assert.Equal(-1, index);
    }

    [Fact]
    public void FindTokenThresholdIndex_ThresholdNotExceeded_ReturnsMinusOne()
    {
        // 100 chars = 25 tokens. Overhead = 4. Total = 29 per message.
        var conversation = new List<LlmMessage>
        {
            LlmMessage.UserText(new string('a', 100))
        };
        var index = TokenEstimator.FindTokenThresholdIndex(conversation, null, 100);
        Assert.Equal(-1, index);
    }

    [Fact]
    public void FindTokenThresholdIndex_ThresholdExceeded_ReturnsIndex()
    {
        // Each message: 4 overhead + 25 content = 29 tokens.
        var conversation = new List<LlmMessage>
        {
            LlmMessage.UserText(new string('a', 100)), // 29 cum
            LlmMessage.UserText(new string('a', 100)), // 58 cum
            LlmMessage.UserText(new string('a', 100)), // 87 cum
            LlmMessage.UserText(new string('a', 100))  // 116 cum
        };

        // Threshold 100. Should be exceeded at index 3 (116 > 100).
        var index = TokenEstimator.FindTokenThresholdIndex(conversation, null, 100);
        Assert.Equal(3, index);
    }

    [Fact]
    public void FindTokenThresholdIndex_WithSystemPrompt_IncludesPromptTokens()
    {
        // System prompt: 4 overhead + 25 content = 29 tokens.
        // Message: 4 overhead + 25 content = 29 tokens.
        // Total at index 0: 29 + 29 = 58.
        var conversation = new List<LlmMessage>
        {
            LlmMessage.UserText(new string('a', 100))
        };

        // Threshold 50. Should be exceeded at index 0 (58 > 50).
        var index = TokenEstimator.FindTokenThresholdIndex(conversation, new string('s', 100), 50);
        Assert.Equal(0, index);
    }
}
