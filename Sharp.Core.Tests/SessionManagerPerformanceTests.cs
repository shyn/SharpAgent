using Sharp.AI;
using Sharp.Core.Sessions;

namespace Sharp.Core.Tests;

public sealed class SessionManagerPerformanceTests : IDisposable
{
    private readonly string _tempDir;

    public SessionManagerPerformanceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharpagent-session-perf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task RebuildContext_ShouldReconstructCorrectly_WithCompaction()
    {
        var manager = await SessionManager.CreateAsync(_tempDir, _tempDir);

        // 1. Add initial messages
        await manager.AppendMessageAsync(LlmMessage.UserText("initial user 1"));
        var keep = await manager.AppendMessageAsync(LlmMessage.AssistantText("initial assistant 1")); // kept
        await manager.AppendMessageAsync(LlmMessage.UserText("initial user 2"));

        // 2. Add compaction
        await manager.AppendCompactionAsync(
            summary: "summary of past",
            firstKeptEntryId: keep.Id,
            tokensBefore: 100);

        // 3. Add more messages
        await manager.AppendBranchSummaryAsync(keep.Id, "branch summary");
        await manager.AppendMessageAsync(LlmMessage.UserText("new user 1"));

        // Expected context:
        // - Compaction Summary
        // - initial assistant 1 (kept)
        // - initial user 2 (between kept and compaction)
        // - Branch Summary
        // - new user 1

        var context = manager.RebuildContext();

        Assert.Equal(5, context.Count);

        // Verify content
        Assert.Contains("summary of past", ((TextContentBlock)context[0].Content[0]).Text);
        Assert.Equal("initial assistant 1", ((TextContentBlock)context[1].Content[0]).Text);
        Assert.Equal("initial user 2", ((TextContentBlock)context[2].Content[0]).Text);
        Assert.Contains("branch summary", ((TextContentBlock)context[3].Content[0]).Text);
        Assert.Equal("new user 1", ((TextContentBlock)context[4].Content[0]).Text);
    }

    [Fact]
    public async Task RebuildContext_ShouldReconstructCorrectly_WithoutCompaction()
    {
        var manager = await SessionManager.CreateAsync(_tempDir, _tempDir);

        await manager.AppendMessageAsync(LlmMessage.UserText("msg1"));
        await manager.AppendMessageAsync(LlmMessage.AssistantText("msg2"));
        await manager.AppendMessageAsync(LlmMessage.UserText("msg3"));

        var context = manager.RebuildContext();

        Assert.Equal(3, context.Count);
        Assert.Equal("msg1", ((TextContentBlock)context[0].Content[0]).Text);
        Assert.Equal("msg2", ((TextContentBlock)context[1].Content[0]).Text);
        Assert.Equal("msg3", ((TextContentBlock)context[2].Content[0]).Text);
    }

    [Fact]
    public async Task RebuildContext_ShouldHandleKeptEntryNotFound()
    {
        // If kept entry ID is invalid, it should behave as if compaction exists but no kept entries are found?
        // Wait, original logic checks if firstKeptIndex >= 0. If not found, it is -1.
        // So no kept entries are added. Only entries after compaction are added.

        var manager = await SessionManager.CreateAsync(_tempDir, _tempDir);

        await manager.AppendMessageAsync(LlmMessage.UserText("msg1"));
        await manager.AppendCompactionAsync("summary", "invalid-id", 100);
        await manager.AppendMessageAsync(LlmMessage.UserText("msg2"));

        var context = manager.RebuildContext();

        Assert.Equal(2, context.Count);
        Assert.Contains("summary", ((TextContentBlock)context[0].Content[0]).Text);
        Assert.Equal("msg2", ((TextContentBlock)context[1].Content[0]).Text);
    }
}
