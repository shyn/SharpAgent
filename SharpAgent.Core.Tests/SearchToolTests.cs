using NSubstitute;
using SharpAgent.Core.Search;
using SharpAgent.Core.Tools;

namespace SharpAgent.Core.Tests;

public class SearchToolTests
{
    [Fact]
    public async Task ExecuteAsync_EmptyQuery_ReturnsError()
    {
        var searchClient = Substitute.For<ISearchClient>();
        var tool = new SearchTool(searchClient);

        var result = await tool.ExecuteAsync("");

        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public async Task ExecuteAsync_JsonEmptyQuery_ReturnsError()
    {
        var searchClient = Substitute.For<ISearchClient>();
        var tool = new SearchTool(searchClient);

        var result = await tool.ExecuteAsync("""{"query": ""}""");

        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public async Task ExecuteAsync_ValidQuery_ReturnsFormattedResults()
    {
        var searchClient = Substitute.For<ISearchClient>();
        searchClient.SearchAsync("test", 5, Arg.Any<CancellationToken>())
            .Returns([
                new SearchResult("Title One", "https://example.com/1", "Snippet one"),
                new SearchResult("Title Two", "https://example.com/2", "Snippet two")
            ]);
        var tool = new SearchTool(searchClient);

        var result = await tool.ExecuteAsync("""{"query": "test", "max_results": 5}""");

        Assert.Contains("[1] Title One", result);
        Assert.Contains("URL: https://example.com/1", result);
        Assert.Contains("Snippet one", result);
        Assert.Contains("[2] Title Two", result);
    }

    [Fact]
    public async Task ExecuteAsync_PlainTextQuery_ParsesCorrectly()
    {
        var searchClient = Substitute.For<ISearchClient>();
        searchClient.SearchAsync("plain query", 5, Arg.Any<CancellationToken>())
            .Returns([new SearchResult("Result", "https://example.com", "Snippet")]);
        var tool = new SearchTool(searchClient);

        var result = await tool.ExecuteAsync("plain query");

        Assert.Contains("[1] Result", result);
        await searchClient.Received(1).SearchAsync("plain query", 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NoResults_ReturnsNoResultsMessage()
    {
        var searchClient = Substitute.For<ISearchClient>();
        searchClient.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var tool = new SearchTool(searchClient);

        var result = await tool.ExecuteAsync("no results query");

        Assert.Equal("No results found.", result);
    }

    [Fact]
    public async Task ExecuteAsync_SearchClientThrows_ReturnsError()
    {
        var searchClient = Substitute.For<ISearchClient>();
        searchClient.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<SearchResult>>(_ => throw new TimeoutException("Connection timed out"));
        var tool = new SearchTool(searchClient);

        var result = await tool.ExecuteAsync("test");

        Assert.StartsWith("Error:", result);
        Assert.Contains("timed out", result);
    }

    [Fact]
    public async Task ExecuteAsync_CustomMaxResults_PassesToSearchClient()
    {
        var searchClient = Substitute.For<ISearchClient>();
        searchClient.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var tool = new SearchTool(searchClient);

        await tool.ExecuteAsync("""{"query": "test", "max_results": 10}""");

        await searchClient.Received(1).SearchAsync("test", 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Name_ReturnsSearch()
    {
        var tool = new SearchTool(Substitute.For<ISearchClient>());

        Assert.Equal("search", tool.Name);
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        var tool = new SearchTool(Substitute.For<ISearchClient>());

        Assert.NotEmpty(tool.Description);
    }
}
