using NSubstitute;
using SharpAgent.Core.Search;

namespace SharpAgent.Core.Tests;

public class DuckDuckGoSearchClientIntegrationTests
{
    [Fact]
    public async Task SearchAsync_Python_ReturnsPythonOrgOrWikipediaInTop5()
    {
        var client = new DuckDuckGoSearchClient();

        var results = await client.SearchAsync("python", 5);

        Assert.NotEmpty(results);
        Assert.True(
            results.Any(r => r.Url.Contains("python.org") || r.Url.Contains("wikipedia.org")),
            $"Expected python.org or wikipedia.org in results. Got: {string.Join(", ", results.Select(r => r.Url))}");
    }
}

public class DuckDuckGoSearchClientTests
{
    private const string SampleHtml = """
        <html>
        <body>
        <a rel="nofollow" class="result__a" href="https://www.example.com/">Example Title</a>
        <a class="result__snippet" href="https://www.example.com/">This is an example snippet with some text.</a>
        <a rel="nofollow" class="result__a" href="https://www.test.org/page">Test Page</a>
        <a class="result__snippet" href="https://www.test.org/page">Another snippet here.</a>
        <a rel="nofollow" class="result__a" href="https://www.third.net/">Third Result</a>
        <a class="result__snippet" href="https://www.third.net/">Third snippet content.</a>
        </body>
        </html>
        """;

    [Fact]
    public void ParseResults_ValidHtml_ReturnsResults()
    {
        var results = DuckDuckGoSearchClient.ParseResults(SampleHtml, 10);

        Assert.Equal(3, results.Count);
        Assert.Equal("Example Title", results[0].Title);
        Assert.Equal("https://www.example.com/", results[0].Url);
        Assert.Equal("This is an example snippet with some text.", results[0].Snippet);
    }

    [Fact]
    public void ParseResults_LimitsResults()
    {
        var results = DuckDuckGoSearchClient.ParseResults(SampleHtml, 2);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void ParseResults_EmptyHtml_ReturnsEmpty()
    {
        var results = DuckDuckGoSearchClient.ParseResults("", 10);

        Assert.Empty(results);
    }

    [Fact]
    public void ParseResults_NoResults_ReturnsEmpty()
    {
        var results = DuckDuckGoSearchClient.ParseResults("<html><body>No results</body></html>", 10);

        Assert.Empty(results);
    }

    [Fact]
    public void ParseResults_HtmlEncodedContent_DecodesCorrectly()
    {
        var html = """
            <a rel="nofollow" class="result__a" href="https://example.com/">Tom &amp; Jerry</a>
            <a class="result__snippet" href="https://example.com/">A &quot;great&quot; show.</a>
            """;

        var results = DuckDuckGoSearchClient.ParseResults(html, 10);

        Assert.Single(results);
        Assert.Equal("Tom & Jerry", results[0].Title);
        Assert.Contains("\"great\"", results[0].Snippet);
    }

    [Fact]
    public void ParseResults_TitleWithHtmlTags_StripsTags()
    {
        var html = """
            <a rel="nofollow" class="result__a" href="https://example.com/"><b>Bold</b> Title</a>
            <a class="result__snippet" href="https://example.com/">Plain snippet.</a>
            """;

        var results = DuckDuckGoSearchClient.ParseResults(html, 10);

        Assert.Single(results);
        Assert.Equal("Bold Title", results[0].Title);
    }

    [Fact]
    public void NormalizeUrl_DirectUrl_ReturnsAsIs()
    {
        var url = DuckDuckGoSearchClient.NormalizeUrl("https://www.example.com/page");

        Assert.Equal("https://www.example.com/page", url);
    }

    [Fact]
    public void NormalizeUrl_ProtocolRelativeUrl_AddsHttps()
    {
        var url = DuckDuckGoSearchClient.NormalizeUrl("//www.example.com/page");

        Assert.Equal("https://www.example.com/page", url);
    }

    [Fact]
    public void NormalizeUrl_UddgRedirect_ExtractsActualUrl()
    {
        var redirectUrl = "//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.example.com%2Fpage&rut=abc";

        var url = DuckDuckGoSearchClient.NormalizeUrl(redirectUrl);

        Assert.Equal("https://www.example.com/page", url);
    }

    [Fact]
    public void NormalizeUrl_EmptyString_ReturnsEmpty()
    {
        var url = DuckDuckGoSearchClient.NormalizeUrl("");

        Assert.Empty(url);
    }

    [Fact]
    public void NormalizeUrl_Null_ReturnsEmpty()
    {
        var url = DuckDuckGoSearchClient.NormalizeUrl(null!);

        Assert.Empty(url);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        var fetcher = Substitute.For<IHtmlFetcher>();
        var client = new DuckDuckGoSearchClient(fetcher);

        var results = await client.SearchAsync("", 5);

        Assert.Empty(results);
        await fetcher.DidNotReceive().FetchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_ValidQuery_CallsFetcherAndParsesResults()
    {
        var fetcher = Substitute.For<IHtmlFetcher>();
        fetcher.FetchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(SampleHtml);
        var client = new DuckDuckGoSearchClient(fetcher);

        var results = await client.SearchAsync("test query", 5);

        Assert.Equal(3, results.Count);
        await fetcher.Received(1).FetchAsync(
            "https://html.duckduckgo.com/html/",
            "q=test%20query",
            30,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_SpecialCharactersInQuery_EncodesCorrectly()
    {
        var fetcher = Substitute.For<IHtmlFetcher>();
        fetcher.FetchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("");
        var client = new DuckDuckGoSearchClient(fetcher);

        await client.SearchAsync("c# .net", 5);

        await fetcher.Received(1).FetchAsync(
            Arg.Any<string>(),
            "q=c%23%20.net",
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }
}
