using Sharp.AI;
using Sharp.Core.Compaction;

namespace Sharp.Core.Tests.Compaction;

public class TokenEstimatorTests
{
    [Fact]
    public void FindTokenThresholdIndex_ReturnsCorrectIndex()
    {
        var conversation = new List<LlmMessage>
        {
            LlmMessage.UserText(new string('a', 10)), // ~3 tokens
            LlmMessage.AssistantText(new string('b', 20)), // ~5 tokens
            LlmMessage.UserText(new string('c', 30)), // ~8 tokens
            LlmMessage.AssistantText(new string('d', 40)) // ~10 tokens
        };

        // Total cumulative roughly:
        // Msg 0: 4 + 3 = 7
        // Msg 1: 7 + 4 + 5 = 16
        // Msg 2: 16 + 4 + 8 = 28
        // Msg 3: 28 + 4 + 10 = 42

        // Threshold 10 -> Should return index 1 (cumulative 16 > 10)
        var index = TokenEstimator.FindTokenThresholdIndex(conversation, null, 10);
        Assert.Equal(1, index);

        // Threshold 20 -> Should return index 2 (cumulative 28 > 20)
        index = TokenEstimator.FindTokenThresholdIndex(conversation, null, 20);
        Assert.Equal(2, index);

        // Threshold 50 -> Should return -1 (cumulative 42 <= 50)
        index = TokenEstimator.FindTokenThresholdIndex(conversation, null, 50);
        Assert.Equal(-1, index);
    }
}
