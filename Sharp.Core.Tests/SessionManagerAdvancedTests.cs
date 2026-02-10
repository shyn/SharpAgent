using Sharp.AI;
using Sharp.Core.Sessions;

namespace Sharp.Core.Tests;

public sealed class SessionManagerAdvancedTests : IDisposable
{
    private readonly string _tempDir;

    public SessionManagerAdvancedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharpagent-session-advanced-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task RebuildContext_WithCompactionBranchSummaryAndCustomMessage_IncludesSyntheticMessages()
    {
        var manager = await SessionManager.CreateAsync(_tempDir, _tempDir);

        _ = await manager.AppendMessageAsync(LlmMessage.UserText("u1"));
        var keep = await manager.AppendMessageAsync(LlmMessage.AssistantText("a1"));
        _ = await manager.AppendMessageAsync(LlmMessage.UserText("u2"));

        _ = await manager.AppendCompactionAsync(
            summary: "compact-summary",
            firstKeptEntryId: keep.Id,
            tokensBefore: 1234);

        _ = await manager.AppendBranchSummaryAsync(fromId: keep.Id, summary: "branch-summary");
        _ = await manager.AppendCustomMessageAsync(customType: "notice", content: "custom-context", display: true);

        var context = manager.RebuildContext();
        var texts = context
            .Select(m => m.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text)
            .Where(t => t != null)
            .ToList();

        Assert.Contains(texts, t => t!.Contains("compact-summary", StringComparison.Ordinal));
        Assert.Contains(texts, t => t == "a1");
        Assert.Contains(texts, t => t == "u2");
        Assert.Contains(texts, t => t!.Contains("branch-summary", StringComparison.Ordinal));
        Assert.Contains(texts, t => t == "custom-context");
    }
}
