using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

namespace SharpAgent.Core.Search;

public sealed partial class DuckDuckGoSearchClient : ISearchClient
{
    private const int TimeoutSeconds = 30;
    private const string SearchUrl = "https://html.duckduckgo.com/html/";

    private readonly IHtmlFetcher _fetcher;

    public DuckDuckGoSearchClient() : this(new CurlHtmlFetcher())
    {
    }

    public DuckDuckGoSearchClient(IHtmlFetcher fetcher)
    {
        _fetcher = fetcher;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var url = SearchUrl;
        var postData = $"q={Uri.EscapeDataString(query)}";
        var html = await _fetcher.FetchAsync(url, postData, TimeoutSeconds, ct);

        return ParseResults(html, maxResults);
    }

    public static IReadOnlyList<SearchResult> ParseResults(string html, int maxResults)
    {
        var results = new List<SearchResult>();

        var linkMatches = ResultLinkRegex().Matches(html);
        var snippetMatches = SnippetRegex().Matches(html);

        for (int i = 0; i < linkMatches.Count && results.Count < maxResults; i++)
        {
            var linkMatch = linkMatches[i];
            var href = WebUtility.HtmlDecode(linkMatch.Groups[1].Value);
            var title = StripHtmlTags(WebUtility.HtmlDecode(linkMatch.Groups[2].Value));

            var url = NormalizeUrl(href);
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(title))
                continue;

            var snippet = "";
            if (i < snippetMatches.Count)
            {
                snippet = StripHtmlTags(WebUtility.HtmlDecode(snippetMatches[i].Groups[1].Value)).Trim();
            }

            results.Add(new SearchResult(title.Trim(), url, snippet));
        }

        return results;
    }

    public static string NormalizeUrl(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return "";

        if (href.Contains("uddg="))
        {
            var uddgMatch = UddgParamRegex().Match(href);
            if (uddgMatch.Success)
                return WebUtility.UrlDecode(uddgMatch.Groups[1].Value);
        }

        if (href.StartsWith("//"))
            return "https:" + href;

        if (href.StartsWith("http"))
            return href;

        return "";
    }

    private static string StripHtmlTags(string html)
    {
        return HtmlTagRegex().Replace(html, "").Trim();
    }

    [GeneratedRegex(@"<a[^>]*class=""result__a""[^>]*href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex ResultLinkRegex();

    [GeneratedRegex(@"<a[^>]*class=""result__snippet""[^>]*>(.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex SnippetRegex();

    [GeneratedRegex(@"uddg=([^&]+)")]
    private static partial Regex UddgParamRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}

public interface IHtmlFetcher
{
    Task<string> FetchAsync(string url, string postData, int timeoutSeconds, CancellationToken ct = default);
}

public sealed class CurlHtmlFetcher : IHtmlFetcher
{
    public async Task<string> FetchAsync(string url, string postData, int timeoutSeconds, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = GetCurlPath(),
            Arguments = $"-s -X POST \"{url}\" -d \"{postData}\" -A \"Mozilla/5.0\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var html = await process.StandardOutput.ReadToEndAsync(linkedCts.Token);
            await process.WaitForExitAsync(linkedCts.Token);
            return html;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Request timed out after {timeoutSeconds} seconds");
        }
    }

    private static string GetCurlPath()
    {
        if (!OperatingSystem.IsWindows())
            return "curl";

        string[] paths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe"),
            @"C:\Windows\System32\curl.exe"
        ];

        foreach (var path in paths)
        {
            if (File.Exists(path))
                return path;
        }

        return "curl";
    }
}
