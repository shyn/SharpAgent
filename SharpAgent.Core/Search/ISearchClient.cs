namespace SharpAgent.Core.Search;

public interface ISearchClient
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default);
}

public sealed record SearchResult(string Title, string Url, string Snippet);
