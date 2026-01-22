using SharpAgent.Core.Sessions;

namespace SharpAgent.Core.Tests;

public class SessionTests
{
    [Fact]
    public void Session_NewSession_HasValidDefaults()
    {
        var session = new Session();

        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.True(session.CreatedAt <= DateTime.UtcNow);
        Assert.Equal(session.CreatedAt, session.UpdatedAt);
        Assert.Null(session.Title);
        Assert.Empty(session.Messages);
    }

    [Fact]
    public void Session_AddMessage_UpdatesTimestamp()
    {
        var session = new Session();
        var originalUpdatedAt = session.UpdatedAt;

        // Small delay to ensure timestamp difference
        Thread.Sleep(10);

        var message = new Message(Role.User, "Hello");
        session.AddMessage(message);

        Assert.Single(session.Messages);
        Assert.Equal(message, session.Messages[0]);
        Assert.True(session.UpdatedAt > originalUpdatedAt);
    }

    [Fact]
    public void Session_AddMessages_AddsAllMessages()
    {
        var session = new Session();
        var messages = new[]
        {
            new Message(Role.User, "Hello"),
            new Message(Role.Assistant, "Hi there!")
        };

        session.AddMessages(messages);

        Assert.Equal(2, session.Messages.Count);
        Assert.Equal("Hello", session.Messages[0].Content);
        Assert.Equal("Hi there!", session.Messages[1].Content);
    }

    [Fact]
    public void Session_Title_CanBeSet()
    {
        var session = new Session { Title = "Test Session" };

        Assert.Equal("Test Session", session.Title);
    }
}

public class JsonSessionStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly JsonSessionStore _store;

    public JsonSessionStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "sharpagent-test-" + Guid.NewGuid());
        _store = new JsonSessionStore(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_ReturnsNewSession()
    {
        var session = await _store.CreateAsync();

        Assert.NotNull(session);
        Assert.NotEqual(Guid.Empty, session.Id);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var session = await _store.CreateAsync();
        session.Title = "Test Session";
        session.AddMessage(new Message(Role.User, "Hello"));
        session.AddMessage(new Message(Role.Assistant, "Hi!", ToolCalls: new[]
        {
            new ToolCall("tc1", "test_tool", "{\"arg\":\"value\"}")
        }));
        session.AddMessage(new Message(Role.Tool, "Tool result", "test_tool", "tc1"));

        await _store.SaveAsync(session);
        var loaded = await _store.LoadAsync(session.Id);

        Assert.NotNull(loaded);
        Assert.Equal(session.Id, loaded!.Id);
        Assert.Equal("Test Session", loaded.Title);
        Assert.Equal(3, loaded.Messages.Count);
        Assert.Equal("Hello", loaded.Messages[0].Content);
        Assert.Single(loaded.Messages[1].ToolCalls!);
        Assert.Equal("tc1", loaded.Messages[1].ToolCalls![0].Id);
    }

    [Fact]
    public async Task LoadAsync_NonExistent_ReturnsNull()
    {
        var result = await _store.LoadAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_RemovesSession()
    {
        var session = await _store.CreateAsync();
        await _store.SaveAsync(session);

        await _store.DeleteAsync(session.Id);
        var result = await _store.LoadAsync(session.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListAsync_ReturnsSummaries()
    {
        var session1 = await _store.CreateAsync();
        session1.Title = "Session 1";
        await _store.SaveAsync(session1);

        var session2 = await _store.CreateAsync();
        session2.Title = "Session 2";
        await _store.SaveAsync(session2);

        var summaries = await _store.ListAsync();

        Assert.Equal(2, summaries.Count);
        Assert.Contains(summaries, s => s.Title == "Session 1");
        Assert.Contains(summaries, s => s.Title == "Session 2");
    }
}
