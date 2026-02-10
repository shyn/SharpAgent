using Sharp.AI;
using Sharp.Core.Sessions;

namespace Sharp.Core.Tests;

public sealed class SessionManagerTests : IDisposable
{
    private readonly string _tempDir;

    public SessionManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharpagent-session-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task AppendAndLoad_RoundTripsMessages()
    {
        var manager = await SessionManager.CreateAsync(_tempDir, _tempDir);
        await manager.AppendMessageAsync(LlmMessage.UserText("hello"));
        await manager.AppendMessageAsync(LlmMessage.AssistantText("world"));

        var loaded = await SessionManager.LoadAsync(manager.SessionFilePath);
        var context = loaded.RebuildContext();

        Assert.Equal(2, context.Count);
        Assert.Equal(LlmMessageRole.User, context[0].Role);
        Assert.Equal(LlmMessageRole.Assistant, context[1].Role);
    }

    [Fact]
    public async Task Branching_SwitchLeafBuildsExpectedContext()
    {
        var manager = await SessionManager.CreateAsync(_tempDir, _tempDir);
        var first = await manager.AppendMessageAsync(LlmMessage.UserText("root"));
        var second = await manager.AppendMessageAsync(LlmMessage.AssistantText("branch-a"));

        manager.SwitchLeaf(first.Id);
        _ = await manager.AppendMessageAsync(LlmMessage.AssistantText("branch-b"));

        var currentContext = manager.RebuildContext();
        Assert.Equal(2, currentContext.Count);
        Assert.Equal("root", ((TextContentBlock)currentContext[0].Content[0]).Text);
        Assert.Equal("branch-b", ((TextContentBlock)currentContext[1].Content[0]).Text);

        var branchA = manager.GetBranch(second.Id);
        Assert.Equal(2, branchA.Count);
    }
}
