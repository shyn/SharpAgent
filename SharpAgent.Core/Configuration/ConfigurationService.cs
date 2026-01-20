using System.Text.Json;

namespace SharpAgent.Core.Configuration;

public sealed class ConfigurationService
{
    private const string ConfigFileName = "config.json";

    private readonly string _configPath;
    private AgentConfig _fileConfig;
    private AgentConfig _effectiveConfig;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public AgentConfig Config => _effectiveConfig;
    public string ConfigPath => _configPath;

    public event EventHandler? ConfigChanged;

    public ConfigurationService(string? configPath = null)
    {
        _configPath = configPath ?? ResolveConfigPath();
        _fileConfig = new AgentConfig();
        _effectiveConfig = new AgentConfig();
    }

    public static string ResolveConfigPath()
    {
        var exeDir = AppContext.BaseDirectory;
        var exeConfigPath = Path.Combine(exeDir, ConfigFileName);

        if (File.Exists(exeConfigPath))
        {
            return exeConfigPath;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "SharpAgent", ConfigFileName);
    }

    public static string GetUserConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "SharpAgent", ConfigFileName);
    }

    public void Load()
    {
        _fileConfig = new AgentConfig();

        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                _fileConfig = JsonSerializer.Deserialize<AgentConfig>(json, JsonOptions) ?? new AgentConfig();
            }
            catch
            {
                _fileConfig = new AgentConfig();
            }
        }

        _effectiveConfig = CloneConfig(_fileConfig);
        ApplyEnvironmentOverrides(_effectiveConfig);
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(_fileConfig, JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    public void Update(Action<AgentConfig> updateAction)
    {
        updateAction(_fileConfig);
        Save();

        _effectiveConfig = CloneConfig(_fileConfig);
        ApplyEnvironmentOverrides(_effectiveConfig);

        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Parses a model string in "provider/model" format.
    /// </summary>
    public static (string ProviderId, string ModelId) ParseModelString(string modelString)
    {
        var parts = modelString.Split('/', 2);
        if (parts.Length != 2)
            throw new ArgumentException($"Invalid model format: '{modelString}'. Expected 'provider/model'.");
        return (parts[0], parts[1]);
    }

    /// <summary>
    /// Gets the provider configuration by ID.
    /// </summary>
    public LlmProviderConfig? GetProviderConfig(string providerId)
    {
        return _effectiveConfig.Providers.FirstOrDefault(p =>
            p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the model configuration from a "provider/model" string.
    /// </summary>
    public (LlmProviderConfig Provider, LlmModelConfig Model)? GetModelConfig(string modelString)
    {
        var (providerId, modelId) = ParseModelString(modelString);
        var provider = GetProviderConfig(providerId);
        if (provider == null) return null;

        var model = provider.Models.FirstOrDefault(m =>
            m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        if (model == null) return null;

        return (provider, model);
    }

    /// <summary>
    /// Creates an LLM client for the specified model string.
    /// </summary>
    public (HttpClient HttpClient, ILlmClient LlmClient) CreateLlmClient(
        string? modelString = null,
        ThinkingConfig? thinkingConfig = null)
    {
        modelString ??= _effectiveConfig.DefaultModel;
        var config = GetModelConfig(modelString);

        if (config == null)
            throw new InvalidOperationException($"Model not found: {modelString}");

        var (provider, model) = config.Value;

        if (string.IsNullOrEmpty(provider.ApiKey))
            throw new InvalidOperationException($"No API key configured for provider: {provider.Id}");

        var baseUrl = provider.BaseUrl;
        if (!baseUrl.EndsWith('/')) baseUrl += "/";

        // Use the first supported API format
        var apiFormat = model.ApiFormats.FirstOrDefault();
        var maxTokens = model.MaxOutputTokens ?? 8192;

        return apiFormat switch
        {
            ApiFormat.Anthropic => CreateAnthropicClient(baseUrl, provider.ApiKey, model.Id, maxTokens, thinkingConfig),
            ApiFormat.OpenAI => CreateOpenAiClient(baseUrl, provider.ApiKey, model.Id),
            _ => CreateOpenAiClient(baseUrl, provider.ApiKey, model.Id)  // Default to OpenAI format
        };
    }

    private static (HttpClient, ILlmClient) CreateAnthropicClient(
        string baseUrl, string apiKey, string model, int maxTokens, ThinkingConfig? thinkingConfig)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            DefaultRequestHeaders =
            {
                { "x-api-key", apiKey },
                { "anthropic-version", "2023-06-01" }
            }
        };
        return (httpClient, new AnthropicClient(httpClient, model, maxTokens, thinkingConfig));
    }

    private static (HttpClient, ILlmClient) CreateOpenAiClient(string baseUrl, string apiKey, string model)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            DefaultRequestHeaders = { { "Authorization", $"Bearer {apiKey}" } }
        };
        return (httpClient, new OpenAiClient(httpClient, model));
    }

    public string GetCurrentModelName()
    {
        return _effectiveConfig.DefaultModel;
    }

    /// <summary>
    /// Gets all available model strings in "provider/model" format.
    /// </summary>
    public IReadOnlyList<string> GetAvailableModels()
    {
        return _effectiveConfig.Providers
            .SelectMany(p => p.Models.Select(m => $"{p.Id}/{m.Id}"))
            .ToList();
    }

    /// <summary>
    /// Sets the current model without persisting to config file.
    /// </summary>
    public void SetCurrentModel(string modelString)
    {
        var config = GetModelConfig(modelString);
        if (config == null)
            throw new InvalidOperationException($"Model not found: {modelString}");

        _effectiveConfig.DefaultModel = modelString;
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool HasApiKey()
    {
        var config = GetModelConfig(_effectiveConfig.DefaultModel);
        return config != null && !string.IsNullOrEmpty(config.Value.Provider.ApiKey);
    }

    public bool HasApiKey(string modelString)
    {
        var config = GetModelConfig(modelString);
        return config != null && !string.IsNullOrEmpty(config.Value.Provider.ApiKey);
    }

    private static AgentConfig CloneConfig(AgentConfig source)
    {
        return new AgentConfig
        {
            DefaultModel = source.DefaultModel,
            Providers = source.Providers.Select(p => new LlmProviderConfig
            {
                Id = p.Id,
                ApiKey = p.ApiKey,
                BaseUrl = p.BaseUrl,
                Models = p.Models.Select(m => new LlmModelConfig
                {
                    Id = m.Id,
                    ApiFormats = [.. m.ApiFormats],
                    ContextWindow = m.ContextWindow,
                    MaxOutputTokens = m.MaxOutputTokens,
                    Capabilities = new LlmCapabilities
                    {
                        ToolCall = m.Capabilities.ToolCall,
                        Image = m.Capabilities.Image,
                        Thinking = m.Capabilities.Thinking,
                        Temperature = m.Capabilities.Temperature,
                        ReasoningEffort = m.Capabilities.ReasoningEffort
                    }
                }).ToList()
            }).ToList()
        };
    }

    private static void ApplyEnvironmentOverrides(AgentConfig config)
    {
        // Override default model
        var defaultModel = Environment.GetEnvironmentVariable("LLM_DEFAULT_MODEL");
        if (!string.IsNullOrEmpty(defaultModel))
            config.DefaultModel = defaultModel;

        // Override OpenAI API key and base URL
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var openAiBaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");
        var openAiProvider = config.Providers.FirstOrDefault(p => p.Id == "openai");
        if (openAiProvider != null)
        {
            if (!string.IsNullOrEmpty(openAiKey))
                openAiProvider.ApiKey = openAiKey;
            if (!string.IsNullOrEmpty(openAiBaseUrl))
                openAiProvider.BaseUrl = openAiBaseUrl;
        }

        // Override Anthropic API key and base URL
        var anthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var anthropicBaseUrl = Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL");
        var anthropicProvider = config.Providers.FirstOrDefault(p => p.Id == "anthropic");
        if (anthropicProvider != null)
        {
            if (!string.IsNullOrEmpty(anthropicKey))
                anthropicProvider.ApiKey = anthropicKey;
            if (!string.IsNullOrEmpty(anthropicBaseUrl))
                anthropicProvider.BaseUrl = anthropicBaseUrl;
        }
    }
}

