using Sharp.AI;
using Sharp.Core.Sessions;

namespace Sharp.Core.Tests;

public sealed class SessionManagerParityTests : IDisposable
{
    private readonly string _tempDir;

    public SessionManagerParityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharpagent-session-parity2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task AppendMessage_CreatesParentIdChain()
    {
        var manager = await SessionManager.CreateAsync(_tempDir, _tempDir);

        var first = await manager.AppendMessageAsync(LlmMessage.UserText("u1"));
        var second = await manager.AppendMessageAsync(LlmMessage.AssistantText("a1"));
        var third = await manager.AppendMessageAsync(LlmMessage.UserText("u2"));

        Assert.Null(first.ParentId);
        Assert.Equal(first.Id, second.ParentId);
        Assert.Equal(second.Id, third.ParentId);
        Assert.Equal(third.Id, manager.CurrentLeafId);
    }

    [Fact]
    public async Task SwitchLeaf_NewAppendsBecomeChildrenOfBranchPoint()
    {
        var manager = await SessionManager.CreateAsync(_tempDir, _tempDir);

        var root = await manager.AppendMessageAsync(LlmMessage.UserText("root"));
        var branchA = await manager.AppendMessageAsync(LlmMessage.AssistantText("branch-a"));

        manager.SwitchLeaf(root.Id);
        var branchB = await manager.AppendMessageAsync(LlmMessage.AssistantText("branch-b"));

        Assert.Equal(root.Id, branchB.ParentId);
        Assert.Equal(branchB.Id, manager.CurrentLeafId);

        var branchAPath = manager.GetBranch(branchA.Id);
        Assert.Equal(["message", "message"], branchAPath.Select(x => x.Type).ToList());

        var branchBPath = manager.GetBranch(branchB.Id);
        Assert.Equal(["message", "message"], branchBPath.Select(x => x.Type).ToList());
        Assert.NotEqual(branchA.Id, branchB.Id);
    }

    [Fact]
    public async Task RebuildContext_FollowsSpecifiedLeaf()
    {
        var manager = await SessionManager.CreateAsync(_tempDir, _tempDir);

        _ = await manager.AppendMessageAsync(LlmMessage.UserText("u1"));
        var a1 = await manager.AppendMessageAsync(LlmMessage.AssistantText("a1"));
        var u2 = await manager.AppendMessageAsync(LlmMessage.UserText("u2"));
        var a2 = await manager.AppendMessageAsync(LlmMessage.AssistantText("a2"));

        manager.SwitchLeaf(a1.Id);
        var ub = await manager.AppendMessageAsync(LlmMessage.UserText("ub"));
        _ = await manager.AppendMessageAsync(LlmMessage.AssistantText("ab"));

        var mainContext = manager.RebuildContext(a2.Id);
        var mainTexts = mainContext
            .Select(m => m.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text)
            .Where(x => x != null)
            .ToList();

        Assert.Equal(["u1", "a1", "u2", "a2"], mainTexts);

        var branchContext = manager.RebuildContext(ub.Id);
        var branchTexts = branchContext
            .Select(m => m.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text)
            .Where(x => x != null)
            .ToList();

        Assert.Equal(["u1", "a1", "ub"], branchTexts);
    }

    [Fact]
    public async Task RebuildContext_WithMultipleCompactions_UsesLatestSummary()
    {
        var manager = await SessionManager.CreateAsync(_tempDir, _tempDir);

        var u1 = await manager.AppendMessageAsync(LlmMessage.UserText("u1"));
        var a1 = await manager.AppendMessageAsync(LlmMessage.AssistantText("a1"));
        var u2 = await manager.AppendMessageAsync(LlmMessage.UserText("u2"));
        _ = await manager.AppendCompactionAsync("summary-1", a1.Id, tokensBefore: 100);
        var a2 = await manager.AppendMessageAsync(LlmMessage.AssistantText("a2"));
        var latestCompaction = await manager.AppendCompactionAsync("summary-2", u2.Id, tokensBefore: 200);

        var context = manager.RebuildContext(latestCompaction.Id);
        var texts = context
            .Select(m => m.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty)
            .ToList();

        Assert.Contains(texts, t => t.Contains("summary-2", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.Contains("summary-1", StringComparison.Ordinal));
        Assert.Contains("u2", texts);
        Assert.Contains("a2", texts);
        Assert.DoesNotContain("u1", texts);
    }

    [Fact]
    public async Task LabelEntries_AreNotIncludedInRebuiltContext()
    {
        var manager = await SessionManager.CreateAsync(_tempDir, _tempDir);

        var user = await manager.AppendMessageAsync(LlmMessage.UserText("hello"));
        _ = await manager.AppendLabelAsync(user.Id, "bookmark");
        _ = await manager.AppendMessageAsync(LlmMessage.AssistantText("world"));

        var context = manager.RebuildContext();
        var texts = context
            .Select(m => m.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text)
            .Where(x => x != null)
            .ToList();

        Assert.Equal(["hello", "world"], texts);
    }

    [Fact]
    public async Task BranchSummaryEntry_IsAttachedToCurrentLeaf()
    {
        var manager = await SessionManager.CreateAsync(_tempDir, _tempDir);

        _ = await manager.AppendMessageAsync(LlmMessage.UserText("u1"));
        var a1 = await manager.AppendMessageAsync(LlmMessage.AssistantText("a1"));
        var u2 = await manager.AppendMessageAsync(LlmMessage.UserText("u2"));
        _ = await manager.AppendMessageAsync(LlmMessage.AssistantText("a2"));

        manager.SwitchLeaf(u2.Id);
        var summary = await manager.AppendBranchSummaryAsync(fromId: a1.Id, summary: "left-branch");

        Assert.Equal(u2.Id, summary.ParentId);

        var context = manager.RebuildContext();
        var texts = context
            .Select(m => m.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text)
            .Where(x => x != null)
            .ToList();

        Assert.Contains(texts, t => t!.Contains("left-branch", StringComparison.Ordinal));
    }
}
