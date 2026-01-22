using System.Text;
using System.Text.Json;
using SharpAgent.Core.Search;

namespace SharpAgent.Core.Tools;

public sealed class SearchTool : ITool
{
    private const int DefaultMaxResults = 5;

    private readonly ISearchClient _searchClient;

    public SearchTool() : this(new DuckDuckGoSearchClient())
    {
    }

    public SearchTool(ISearchClient searchClient)
    {
        _searchClient = searchClient;
    }

    public string Name => "search";
    public string? WorkingDirectory { get; set; }
    public string Description =>
        "Search the web using DuckDuckGo. Returns titles, URLs, and snippets for the top results.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "The search query" },
            max_results = new { type = "integer", description = $"Maximum number of results to return (default: {DefaultMaxResults})" }
        },
        required = new[] { "query" }
    };

    public async Task<string> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var (query, maxResults) = ParseInput(input);

            if (string.IsNullOrWhiteSpace(query))
                return "Error: query is required";

            var results = await _searchClient.SearchAsync(query, maxResults, ct);

            if (results.Count == 0)
                return "No results found.";

            return FormatResults(results);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static string FormatResults(IReadOnlyList<SearchResult> results)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"[{i + 1}] {r.Title}");
            sb.AppendLine($"    URL: {r.Url}");
            if (!string.IsNullOrEmpty(r.Snippet))
                sb.AppendLine($"    {r.Snippet}");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static (string query, int maxResults) ParseInput(string input)
    {
        input = input.Trim();
        if (!input.StartsWith('{'))
            return (input, DefaultMaxResults);

        using var doc = JsonDocument.Parse(input);
        var root = doc.RootElement;

        var query = root.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
        var maxResults = root.TryGetProperty("max_results", out var m) && m.TryGetInt32(out var mv)
            ? mv
            : DefaultMaxResults;

        return (query, maxResults);
    }
}
