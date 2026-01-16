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

    private static AgentConfig CloneConfig(AgentConfig source)
    {
        return new AgentConfig
        {
            Provider = source.Provider,
            OpenAi = new OpenAiConfig
            {
                ApiKey = source.OpenAi.ApiKey,
                BaseUrl = source.OpenAi.BaseUrl,
                Model = source.OpenAi.Model
            },
            Anthropic = new AnthropicConfig
            {
                ApiKey = source.Anthropic.ApiKey,
                BaseUrl = source.Anthropic.BaseUrl,
                Model = source.Anthropic.Model,
                MaxTokens = source.Anthropic.MaxTokens
            }
        };
    }

    private static void ApplyEnvironmentOverrides(AgentConfig config)
    {
        var provider = Environment.GetEnvironmentVariable("LLM_PROVIDER");
        if (!string.IsNullOrEmpty(provider))
            config.Provider = provider.ToLowerInvariant();

        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(openAiKey))
            config.OpenAi.ApiKey = openAiKey;

        var openAiBaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");
        if (!string.IsNullOrEmpty(openAiBaseUrl))
            config.OpenAi.BaseUrl = openAiBaseUrl;

        var openAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        if (!string.IsNullOrEmpty(openAiModel))
            config.OpenAi.Model = openAiModel;

        var anthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrEmpty(anthropicKey))
            config.Anthropic.ApiKey = anthropicKey;

        var anthropicBaseUrl = Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL");
        if (!string.IsNullOrEmpty(anthropicBaseUrl))
            config.Anthropic.BaseUrl = anthropicBaseUrl;

        var anthropicModel = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL");
        if (!string.IsNullOrEmpty(anthropicModel))
            config.Anthropic.Model = anthropicModel;
    }

    public (HttpClient HttpClient, ILlmClient LlmClient) CreateLlmClient()
    {
        var isAnthropic = _effectiveConfig.Provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase);

        if (isAnthropic)
        {
            var baseUrl = _effectiveConfig.Anthropic.BaseUrl;
            if (!baseUrl.EndsWith('/')) baseUrl += "/";

            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                DefaultRequestHeaders =
                {
                    { "x-api-key", _effectiveConfig.Anthropic.ApiKey ?? "" },
                    { "anthropic-version", "2023-06-01" }
                }
            };

            return (httpClient, new AnthropicClient(httpClient, _effectiveConfig.Anthropic.Model, _effectiveConfig.Anthropic.MaxTokens));
        }
        else
        {
            var baseUrl = _effectiveConfig.OpenAi.BaseUrl;
            if (!baseUrl.EndsWith('/')) baseUrl += "/";

            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                DefaultRequestHeaders = { { "Authorization", $"Bearer {_effectiveConfig.OpenAi.ApiKey ?? ""}" } }
            };

            return (httpClient, new OpenAiClient(httpClient, _effectiveConfig.OpenAi.Model));
        }
    }

    public string GetCurrentModelName()
    {
        return _effectiveConfig.Provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase)
            ? _effectiveConfig.Anthropic.Model
            : _effectiveConfig.OpenAi.Model;
    }

    public bool HasApiKey()
    {
        return _effectiveConfig.Provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase)
            ? !string.IsNullOrEmpty(_effectiveConfig.Anthropic.ApiKey)
            : !string.IsNullOrEmpty(_effectiveConfig.OpenAi.ApiKey);
    }
}
