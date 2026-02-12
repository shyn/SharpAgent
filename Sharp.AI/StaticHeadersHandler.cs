namespace Sharp.AI;

internal sealed class StaticHeadersHandler : DelegatingHandler
{
    private readonly IReadOnlyDictionary<string, string> _headers;

    public StaticHeadersHandler(
        HttpMessageHandler innerHandler,
        IReadOnlyDictionary<string, string> headers)
        : base(innerHandler)
    {
        _headers = headers ?? throw new ArgumentNullException(nameof(headers));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        foreach (var (key, value) in _headers)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                continue;

            if (request.Headers.Contains(key))
                continue;

            request.Headers.TryAddWithoutValidation(key, value);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
