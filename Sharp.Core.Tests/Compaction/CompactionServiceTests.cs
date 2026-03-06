using System.Text.Json;
using Sharp.AI;
using Sharp.Core.Compaction;
using Sharp.Core.Sessions;
using Sharp.Core.Tests.TestDoubles;

namespace Sharp.Core.Tests.Compaction;

public class CompactionServiceTests
{
    [Fact]
    public void CompactionSettings_DefaultValues_AreCorrect()
    {
        var settings = new CompactionSettings();

        Assert.Equal(16000, settings.ReserveTokens);
        Assert.Equal(20000, settings.KeepRecentTokens);
        Assert.Equal(0.9, settings.ThresholdRatio);
        Assert.Equal(1000, settings.MinTokensForCompaction);
        Assert.Equal(4000, settings.MinTokensToCompact);
    }

    [Fact]
    public void CompactionSettings_With_CreatesModifiedCopy()
    {
        var original = new CompactionSettings();
        var modified = original.With(reserveTokens: 10000, keepRecentTokens: 15000);

        Assert.Equal(16000, original.ReserveTokens);
        Assert.Equal(20000, original.KeepRecentTokens);

        Assert.Equal(10000, modified.ReserveTokens);
        Assert.Equal(15000, modified.KeepRecentTokens);
    }

    [Fact]
    public void CompactionResult_TokensSaved_CalculatesCorrectly()
    {
        var result = new CompactionResult(
            Summary: "test summary",
            FirstKeptEntryId: "entry1",
            TokensBefore: 10000,
            TokensAfter: 2000,
            CompactedEntryIds: ["a", "b", "c"]);

        Assert.Equal(8000, result.TokensSaved);
        Assert.Equal(0.8, result.SavingsRatio);
    }

    [Fact]
    public void TokenEstimator_EstimateTokens_EmptyString_ReturnsZero()
    {
        Assert.Equal(0, TokenEstimator.EstimateTokens(""));
        Assert.Equal(0, TokenEstimator.EstimateTokens(null));
    }

    [Fact]
    public void TokenEstimator_EstimateTokens_UsesCharacterRatio()
    {
        var text = new string('a', 400); // 400 chars should be ~100 tokens

        var tokens = TokenEstimator.EstimateTokens(text);

        Assert.Equal(100, tokens);
    }

    [Fact]
    public void TokenEstimator_EstimateMessageTokens_TextOnly()
    {
        var message = LlmMessage.UserText(new string('a', 400)); // 400 chars

        var tokens = TokenEstimator.EstimateMessageTokens(message);

        // Message overhead (4) + 400/4 = 104 tokens
        Assert.Equal(104, tokens);
    }

    [Fact]
    public void TokenEstimator_EstimateConversationTokens_WithSystemPrompt()
    {
        var conversation = new List<LlmMessage>
        {
            LlmMessage.UserText(new string('a', 400))
        };

        var tokens = TokenEstimator.EstimateConversationTokens(conversation, new string('b', 400));

        // System prompt: 4 + 100 = 104
        // User message: 4 + 100 = 104
        // Total: 208
        Assert.Equal(208, tokens);
    }

    [Fact]
    public void TokenEstimator_IsApproachingLimit_WhenOverThreshold_ReturnsTrue()
    {
        var conversation = new List<LlmMessage>
        {
            LlmMessage.UserText(new string('a', 1000)) // 1000 chars = 250 tokens
        };

        var result = TokenEstimator.IsApproachingLimit(
            conversation,
            null,
            contextWindow: 200, // Low context window
            thresholdRatio: 0.9);

        Assert.True(result);
    }

    [Fact]
    public void FileOperations_WithReadFile_AddsToSet()
    {
        var ops = new FileOperations()
            .WithReadFile("/path/to/file.txt")
            .WithReadFile("/path/to/another.txt");

        Assert.Equal(2, ops.ReadFiles.Count);
        Assert.Contains("/path/to/file.txt", ops.ReadFiles);
    }

    [Fact]
    public void FileOperations_WithCreatedFile_AddsToSet()
    {
        var ops = new FileOperations().WithCreatedFile("/path/new.txt");

        Assert.Single(ops.CreatedFiles);
        Assert.Contains("/path/new.txt", ops.CreatedFiles);
    }

    [Fact]
    public void FileOperations_Merge_CombinesOperations()
    {
        var ops1 = new FileOperations()
            .WithReadFile("/path/file1.txt")
            .WithCreatedFile("/path/new.txt");

        var ops2 = new FileOperations()
            .WithReadFile("/path/file2.txt")
            .WithEditedFile("/path/existing.txt");

        var merged = ops1.Merge(ops2);

        Assert.Equal(2, merged.ReadFiles.Count);
        Assert.Single(merged.CreatedFiles);
        Assert.Single(merged.EditedFiles);
    }

    [Fact]
    public void FileOperations_AllFiles_ReturnsDistinctPaths()
    {
        var ops = new FileOperations()
            .WithReadFile("/path/file.txt")
            .WithEditedFile("/path/file.txt");

        Assert.Single(ops.AllFiles);
    }

    [Fact]
    public async Task CompactAsync_WithNonMessageEntries_MapsCutPointToEntryBoundary()
    {
        var provider = new RecordingProvider();
        provider.Enqueue(
            new LlmTextDeltaEvent("summary"),
            new LlmCompletedEvent("summary", null, []));

        var settings = new CompactionSettings
        {
            ReserveTokens = 0,
            KeepRecentTokens = 205,
            ThresholdRatio = 0.5,
            MinTokensForCompaction = 1,
            MinTokensToCompact = 1
        };

        var service = new CompactionService(provider, settings);
        var entries = CreateEntriesWithInterleavedNonMessage();
        var model = new ModelDescriptor(
            ProviderId: "recording",
            ModelId: "test-model",
            ApiKind: ProviderApiKind.OpenAiChatCompletions,
            ContextWindow: 450,
            MaxOutputTokens: 256);

        var result = await service.CompactAsync(entries, model);

        Assert.NotNull(result);
        Assert.Equal("m3", result!.FirstKeptEntryId);
        Assert.Collection(
            result.CompactedEntryIds,
            id => Assert.Equal("m1", id),
            id => Assert.Equal("cfg1", id),
            id => Assert.Equal("m2", id));
    }

    private static IReadOnlyList<SessionEntryEnvelope> CreateEntriesWithInterleavedNonMessage()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            CreateMessageEntry("m1", null, now, LlmMessage.UserText(new string('a', 400))),
            CreateModelChangeEntry("cfg1", "m1", now.AddSeconds(1), "openai", "gpt-4o-mini"),
            CreateMessageEntry("m2", "cfg1", now.AddSeconds(2), LlmMessage.AssistantText(new string('b', 400))),
            CreateMessageEntry("m3", "m2", now.AddSeconds(3), LlmMessage.UserText(new string('c', 400))),
            CreateMessageEntry("m4", "m3", now.AddSeconds(4), LlmMessage.AssistantText(new string('d', 400)))
        ];
    }

    private static SessionEntryEnvelope CreateMessageEntry(
        string id,
        string? parentId,
        DateTimeOffset timestamp,
        LlmMessage message)
        => new(
            type: "message",
            id: id,
            parentId: parentId,
            timestampUtc: timestamp,
            payload: JsonSerializer.SerializeToElement(new MessageEntryPayload(message), JsonDefaults.Options));

    private static SessionEntryEnvelope CreateModelChangeEntry(
        string id,
        string? parentId,
        DateTimeOffset timestamp,
        string provider,
        string modelId)
        => new(
            type: "model_change",
            id: id,
            parentId: parentId,
            timestampUtc: timestamp,
            payload: JsonSerializer.SerializeToElement(new ModelChangeEntryPayload(provider, modelId), JsonDefaults.Options));
}
