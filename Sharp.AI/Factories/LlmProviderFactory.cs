namespace Sharp.AI.Factories;

public static class LlmProviderFactory
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<ProviderApiKind, Func<LlmProviderCreateContext, ILlmProvider>> Factories = [];

    static LlmProviderFactory()
    {
        Register(
            ProviderApiKind.OpenAiChatCompletions,
            context => CreateOpenAi(CreateHttpClient(
                context.Model,
                context.BaseUrl,
                context.Handler,
                ResolveCredentialProvider(context),
                context.Headers)),
            overwrite: true);

        Register(
            ProviderApiKind.OpenAiResponses,
            context => CreateOpenAiResponses(CreateHttpClient(
                context.Model,
                context.BaseUrl,
                context.Handler,
                ResolveCredentialProvider(context),
                context.Headers)),
            overwrite: true);

        Register(
            ProviderApiKind.AnthropicMessages,
            context => CreateAnthropic(CreateHttpClient(
                context.Model,
                context.BaseUrl,
                context.Handler,
                ResolveCredentialProvider(context),
                context.Headers)),
            overwrite: true);
    }

    public static void Register(
        ProviderApiKind apiKind,
        Func<LlmProviderCreateContext, ILlmProvider> factory,
        bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(factory);

        lock (SyncRoot)
        {
            if (!overwrite && Factories.ContainsKey(apiKind))
                throw new InvalidOperationException($"Provider factory already registered for API kind '{apiKind}'");

            Factories[apiKind] = factory;
        }
    }

    public static bool Unregister(ProviderApiKind apiKind)
    {
        lock (SyncRoot)
        {
            return Factories.Remove(apiKind);
        }
    }

    public static IReadOnlyCollection<ProviderApiKind> RegisteredApiKinds
    {
        get
        {
            lock (SyncRoot)
            {
                return Factories.Keys.ToList();
            }
        }
    }

    public static ILlmProvider Create(
        ModelDescriptor model,
        string apiKey,
        string baseUrl,
        HttpMessageHandler? handler = null)
        => Create(model, apiKey, baseUrl, credentialProvider: null, handler);

    public static ILlmProvider Create(
        ModelDescriptor model,
        string apiKey,
        string baseUrl,
        ILlmCredentialProvider? credentialProvider,
        HttpMessageHandler? handler = null)
    {
        if (credentialProvider == null && string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"Missing API key for provider '{model.ProviderId}'");

        Func<LlmProviderCreateContext, ILlmProvider>? factory;
        lock (SyncRoot)
        {
            Factories.TryGetValue(model.ApiKind, out factory);
        }

        if (factory == null)
            throw new ArgumentOutOfRangeException(nameof(model.ApiKind), model.ApiKind, "Unknown provider API kind");

        return factory(new LlmProviderCreateContext(model, apiKey, baseUrl, handler, credentialProvider, model.Headers));
    }

    private static ILlmCredentialProvider ResolveCredentialProvider(LlmProviderCreateContext context)
    {
        if (context.CredentialProvider != null)
            return context.CredentialProvider;

        if (string.IsNullOrWhiteSpace(context.ApiKey))
            throw new InvalidOperationException($"Missing API key for provider '{context.Model.ProviderId}'");

        return new StaticApiKeyCredentialProvider(context.ApiKey);
    }

    private static HttpClient CreateHttpClient(
        ModelDescriptor model,
        string baseUrl,
        HttpMessageHandler? handler,
        ILlmCredentialProvider credentialProvider,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        if (!baseUrl.EndsWith('/'))
            baseUrl += "/";

        HttpMessageHandler innerHandler = handler ?? new HttpClientHandler();

        if (headers is { Count: > 0 })
            innerHandler = new StaticHeadersHandler(innerHandler, headers);

        var pipeline = new CredentialInjectionHandler(
            innerHandler,
            credentialProvider,
            new LlmCredentialContext(model, baseUrl));

        var httpClient = new HttpClient(pipeline, disposeHandler: true);
        httpClient.BaseAddress = new Uri(baseUrl);
        return httpClient;
    }

    private static ILlmProvider CreateOpenAi(HttpClient httpClient)
        => new Providers.OpenAiLlmProvider(httpClient);

    private static ILlmProvider CreateOpenAiResponses(HttpClient httpClient)
        => new Providers.OpenAiResponsesLlmProvider(httpClient);

    private static ILlmProvider CreateAnthropic(HttpClient httpClient)
        => new Providers.AnthropicLlmProvider(httpClient);
}
