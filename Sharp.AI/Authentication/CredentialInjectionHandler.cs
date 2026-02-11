namespace Sharp.AI.Authentication;

internal sealed class CredentialInjectionHandler : DelegatingHandler
{
    private readonly ILlmCredentialProvider _credentialProvider;
    private readonly LlmCredentialContext _credentialContext;

    public CredentialInjectionHandler(
        HttpMessageHandler innerHandler,
        ILlmCredentialProvider credentialProvider,
        LlmCredentialContext credentialContext)
        : base(innerHandler)
    {
        _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
        _credentialContext = credentialContext;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = await _credentialProvider.GetHeadersAsync(_credentialContext, cancellationToken);
        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key) || string.IsNullOrWhiteSpace(header.Value))
                continue;

            if (request.Headers.Contains(header.Key))
                continue;

            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
