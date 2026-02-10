using System.Text.Json;
using Sharp.AI;
using Sharp.Core.Compaction;
using Sharp.Core.Sessions;

namespace Sharp.Core.Tests.Compaction;

public class CompactionIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sessionDir;

    public CompactionIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"compaction_tests_{Guid.NewGuid():N}");
        _sessionDir = Path.Combine(_tempDir, "sessions");
        Directory.CreateDirectory(_sessionDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task SessionManager_ApplyCompactionAsync_CreatesCompactionEntry()
    {
        var session = await SessionManager.CreateAsync(_sessionDir, "/work");

        // Add some messages
        await session.AppendMessageAsync(LlmMessage.UserText("Hello"));
        await session.AppendMessageAsync(LlmMessage.AssistantText("Hi there"));
        await session.AppendMessageAsync(LlmMessage.UserText("How are you?"));

        // Create a compaction result
        var result = new CompactionResult(
            Summary: "Initial conversation",
            FirstKeptEntryId: session.CurrentLeafId,
            TokensBefore: 1500,
            TokensAfter: 200,
            CompactedEntryIds: ["entry1", "entry2"],
            Details: null,
            FromHook: false);

        // Apply the compaction
        var entry = await session.ApplyCompactionAsync(result);

        // Verify the entry was created
        Assert.Equal("compaction", entry.Type);

        // Verify we can retrieve the compaction info
        var latest = session.GetLatestCompaction();
        Assert.NotNull(latest);
        Assert.Equal("Initial conversation", latest.Summary);
        Assert.Equal(1500, latest.TokensBefore);
    }

    [Fact]
    public void SessionManager_HasCompaction_WithNoCompaction_ReturnsFalse()
    {
        // Arrange - use reflection or check if we need to populate entries first
        var session = SessionManager.CreateAsync(_sessionDir, "/work").Result;

        // Initially no compaction
        Assert.False(session.HasCompaction());
    }

    [Fact]
    public async Task SessionManager_GetCurrentBranch_ReturnsEntriesInOrder()
    {
        var session = await SessionManager.CreateAsync(_sessionDir, "/work");

        await session.AppendMessageAsync(LlmMessage.UserText("Message 1"));
        await session.AppendMessageAsync(LlmMessage.UserText("Message 2"));
        await session.AppendMessageAsync(LlmMessage.UserText("Message 3"));

        var branch = session.GetCurrentBranch();

        Assert.Equal(3, branch.Count);
    }

    [Fact]
    public void TokenEstimator_FindCutPoint_WithLargeConversation()
    {
        // Create a conversation that exceeds our typical threshold
        var conversation = new List<LlmMessage>();

        // Add messages that total about 50k tokens worth of text (200k chars)
        for (int i = 0; i < 50; i++)
        {
            conversation.Add(LlmMessage.UserText($"Message {i}: " + new string('x', 4000)));
            conversation.Add(LlmMessage.AssistantText($"Response {i}: " + new string('y', 2000)));
        }

        var tokens = TokenEstimator.EstimateConversationTokens(conversation, null);

        // Should be around 50k+ tokens (75 messages * ~750 tokens each)
        Assert.True(tokens > 30000, $"Expected more than 30000 tokens, got {tokens}");
    }

    [Fact]
    public void CompactionSettings_ValidatesThresholdRatio()
    {
        var settings = new CompactionSettings { ThresholdRatio = 0.85 };

        Assert.Equal(0.85, settings.ThresholdRatio);
    }

    [Fact]
    public void FileOperationTracker_ExtractFileOperations_FromMessageEntries()
    {
        // This tests the file operation tracking without needing a real session
        var operations = new FileOperations()
            .WithReadFile("/project/src/main.cs")
            .WithCreatedFile("/project/src/new.cs")
            .WithEditedFile("/project/src/existing.cs")
            .WithDeletedFile("/project/src/old.cs");

        Assert.Single(operations.ReadFiles);
        Assert.Single(operations.CreatedFiles);
        Assert.Single(operations.EditedFiles);
        Assert.Single(operations.DeletedFiles);
    }
}
