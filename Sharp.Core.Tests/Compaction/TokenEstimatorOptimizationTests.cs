using Sharp.AI;
using Sharp.Core.Compaction;

namespace Sharp.Core.Tests.Compaction;

public class TokenEstimatorOptimizationTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("ab", 1)]
    [InlineData("abc", 1)]
    [InlineData("abcd", 1)]
    [InlineData("abcde", 2)]
    [InlineData("abcdefgh", 2)]
    [InlineData("abcdefghi", 3)]
    public void EstimateTokens_ReturnsCorrectValues(string? text, int expected)
    {
        Assert.Equal(expected, TokenEstimator.EstimateTokens(text));
    }

    [Fact]
    public void EstimateTokens_MatchesMathCeiling()
    {
        for (int i = 0; i < 100; i++)
        {
            var text = new string('a', i);
            var expected = (int)Math.Ceiling(i / 4.0);
            Assert.Equal(expected, TokenEstimator.EstimateTokens(text));
        }
    }

    [Fact]
    public void FindTokenThresholdIndex_ReturnsCorrectIndex()
    {
        // Setup conversation
        // System prompt: "system" (6 chars) -> (6+3)/4 = 2 tokens. +4 overhead = 6 tokens.
        // Message 1: "msg1" (4 chars) -> 1 token. +4 overhead = 5 tokens.
        // Message 2: "msg2" (4 chars) -> 1 token. +4 overhead = 5 tokens.
        // Message 3: "msg3" (4 chars) -> 1 token. +4 overhead = 5 tokens.

        // Cumulative:
        // System: 6
        // + M1: 6+5=11
        // + M2: 11+5=16
        // + M3: 16+5=21

        var systemPrompt = "system";
        var conversation = new List<LlmMessage>
        {
            LlmMessage.UserText("msg1"),
            LlmMessage.UserText("msg2"),
            LlmMessage.UserText("msg3")
        };

        // Case 1: Threshold 10.
        // i=0: cum=11 > 10. Returns 0. Correct.
        Assert.Equal(0, TokenEstimator.FindTokenThresholdIndex(conversation, systemPrompt, 10));

        // Case 2: Threshold 15.
        // i=0: cum=11 <= 15.
        // i=1: cum=11+5=16 > 15. Returns 1. Correct.
        Assert.Equal(1, TokenEstimator.FindTokenThresholdIndex(conversation, systemPrompt, 15));

        // Case 3: Threshold 20.
        // i=1: cum=16 <= 20.
        // i=2: cum=16+5=21 > 20. Returns 2. Correct.
        Assert.Equal(2, TokenEstimator.FindTokenThresholdIndex(conversation, systemPrompt, 20));

        // Case 4: Threshold 25.
        // i=2: cum=21 <= 25.
        // Returns -1. Correct.
        Assert.Equal(-1, TokenEstimator.FindTokenThresholdIndex(conversation, systemPrompt, 25));

        // Case 5: Threshold 2. (System prompt exceeds)
        // i=0: cum=11 > 2. Returns 0.
        Assert.Equal(0, TokenEstimator.FindTokenThresholdIndex(conversation, systemPrompt, 2));
    }

    [Fact]
    public void FindTokenThresholdIndex_NoSystemPrompt()
    {
        // M1: 5 tokens
        // M2: 5 tokens

        var conversation = new List<LlmMessage>
        {
            LlmMessage.UserText("msg1"),
            LlmMessage.UserText("msg2")
        };

        // Threshold 3.
        // i=0: cum=5 > 3. Returns 0.
        Assert.Equal(0, TokenEstimator.FindTokenThresholdIndex(conversation, null, 3));

        // Threshold 7.
        // i=0: cum=5.
        // i=1: cum=10 > 7. Returns 1.
        Assert.Equal(1, TokenEstimator.FindTokenThresholdIndex(conversation, null, 7));
    }
}
