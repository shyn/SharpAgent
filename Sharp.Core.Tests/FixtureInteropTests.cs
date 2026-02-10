using Sharp.AI;
using Sharp.Core.Sessions;
using Sharp.Core.Tests.TestDoubles;

namespace Sharp.Core.Tests;

public sealed class FixtureInteropTests : IDisposable
{
    private readonly string _tempDir;

    public FixtureInteropTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharpagent-fixture-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task LargeSessionFixture_CanBeImportedIntoSessionManager()
    {
        var manager = await SessionManager.CreateAsync(_tempDir, _tempDir);

        await PiFixtureSessionImporter.ImportJsonlAsync(
            FixturePaths.Get("large-session.jsonl"),
            manager);

        var context = manager.RebuildContext();
        Assert.True(context.Count > 100);
        Assert.Contains(context, x => x.Role == LlmMessageRole.User);
        Assert.Contains(context, x => x.Role == LlmMessageRole.Assistant);
        Assert.Contains(context, x => x.Role == LlmMessageRole.Tool);
        Assert.Contains(manager.Entries, x => x.Type == "model_change");
    }

    [Fact]
    public async Task BeforeCompactionFixture_UsesLatestCompactionSummaryInRebuiltContext()
    {
        var manager = await SessionManager.CreateAsync(_tempDir, _tempDir);

        await PiFixtureSessionImporter.ImportJsonlAsync(
            FixturePaths.Get("before-compaction.jsonl"),
            manager);

        var compactionEntries = manager.Entries.Where(x => x.Type == "compaction").ToList();
        Assert.True(compactionEntries.Count >= 2);

        var context = manager.RebuildContext();
        Assert.NotEmpty(context);

        var firstText = Assert.IsType<TextContentBlock>(context[0].Content[0]).Text;
        Assert.Contains("compacted into the following summary", firstText, StringComparison.Ordinal);
        Assert.Contains("Context Checkpoint: Coding Agent Refactoring", firstText, StringComparison.Ordinal);
    }

    [Fact]
    public void AssistantMessageFixture_CanBeConvertedToSharpMessage()
    {
        var message = PiFixtureSessionImporter.LoadAssistantMessage(
            FixturePaths.Get("assistant-message-with-thinking-code.json"));

        Assert.Equal(LlmMessageRole.Assistant, message.Role);
        Assert.Contains(message.Content, block => block is ThinkingContentBlock);
        Assert.Contains(message.Content, block => block is TextContentBlock text && !string.IsNullOrWhiteSpace(text.Text));
    }
}
