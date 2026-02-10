namespace Sharp.AI;

public static class LlmProviderFactory
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<ProviderApiKind, Func<LlmProviderCreateContext, ILlmProvider>> Factories = [];

    static LlmProviderFactory()
    {
        Register(
            ProviderApiKind.OpenAiChatCompletions,
            context => CreateOpenAi(CreateHttpClient(context.BaseUrl, context.Handler), context.ApiKey),
            overwrite: true);

        Register(
            ProviderApiKind.AnthropicMessages,
            context => CreateAnthropic(CreateHttpClient(context.BaseUrl, context.Handler), context.ApiKey),
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
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"Missing API key for provider '{model.ProviderId}'");

        Func<LlmProviderCreateContext, ILlmProvider>? factory;
        lock (SyncRoot)
        {
            Factories.TryGetValue(model.ApiKind, out factory);
        }

        if (factory == null)
            throw new ArgumentOutOfRangeException(nameof(model.ApiKind), model.ApiKind, "Unknown provider API kind");

        return factory(new LlmProviderCreateContext(model, apiKey, baseUrl, handler));
    }

    private static HttpClient CreateHttpClient(string baseUrl, HttpMessageHandler? handler)
    {
        if (!baseUrl.EndsWith('/'))
            baseUrl += "/";

        var httpClient = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        httpClient.BaseAddress = new Uri(baseUrl);
        return httpClient;
    }

    private static ILlmProvider CreateOpenAi(HttpClient httpClient, string apiKey)
    {
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        return new Providers.OpenAiLlmProvider(httpClient);
    }

    private static ILlmProvider CreateAnthropic(HttpClient httpClient, string apiKey)
    {
        httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        return new Providers.AnthropicLlmProvider(httpClient);
    }
}
