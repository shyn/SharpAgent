using System.Text.Json;
using System.Text;
using Sharp.AI;

namespace Sharp.Core.Configuration;

public sealed class AgentConfigValidationResult
{
    public AgentConfigValidationResult(
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings)
    {
        Errors = errors;
        Warnings = warnings;
    }

    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<string> Warnings { get; }
    public bool IsValid => Errors.Count == 0;
}

public sealed class AgentConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public AgentConfigurationService(AgentConfig config)
    {
        Config = config;
    }

    public AgentConfig Config { get; }

    public static AgentConfigurationService LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return new AgentConfigurationService(ApplyEnvironmentOverrides(new AgentConfig()));

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<AgentConfig>(json, JsonOptions) ?? new AgentConfig();
        return new AgentConfigurationService(ApplyEnvironmentOverrides(config));
    }

    public AgentConfigValidationResult ValidateConfig()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(Config.DefaultModel))
            errors.Add("defaultModel is required and must use '<provider>/<model>' format.");

        if (Config.Providers.Count == 0)
            errors.Add("At least one provider must be configured.");

        var providersById = new Dictionary<string, ProviderConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in Config.Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Id))
            {
                errors.Add("Provider id cannot be empty.");
                continue;
            }

            if (!providersById.TryAdd(provider.Id, provider))
                errors.Add($"Duplicate provider id '{provider.Id}'.");

            var resolvedBaseUrl = ResolveProviderBaseUrl(provider);
            if (string.IsNullOrWhiteSpace(resolvedBaseUrl))
            {
                errors.Add($"Provider '{provider.Id}' must set baseUrl.");
            }
            else if (!Uri.TryCreate(resolvedBaseUrl, UriKind.Absolute, out var baseUrl) ||
                     (baseUrl.Scheme != Uri.UriSchemeHttps && baseUrl.Scheme != Uri.UriSchemeHttp))
            {
                errors.Add($"Provider '{provider.Id}' has invalid baseUrl '{resolvedBaseUrl}'.");
            }

            if (provider.Models.Count == 0)
                errors.Add($"Provider '{provider.Id}' must configure at least one model.");

            var modelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var model in provider.Models)
            {
                if (string.IsNullOrWhiteSpace(model.Id))
                {
                    errors.Add($"Provider '{provider.Id}' has a model with empty id.");
                    continue;
                }

                if (!modelIds.Add(model.Id))
                    errors.Add($"Provider '{provider.Id}' has duplicate model id '{model.Id}'.");

                if (model.ContextWindow is <= 0)
                    errors.Add(
                        $"Provider '{provider.Id}' model '{model.Id}' has invalid contextWindow '{model.ContextWindow}'.");

                if (model.MaxOutputTokens is <= 0)
                    errors.Add(
                        $"Provider '{provider.Id}' model '{model.Id}' has invalid maxOutputTokens '{model.MaxOutputTokens}'.");

                if (model.Api is not null)
                    warnings.Add(
                        $"Provider '{provider.Id}' model '{model.Id}' uses legacy field 'api'; move it to provider-level 'api'.");
            }
        }

        if (!TryParseModelString(Config.DefaultModel, out var defaultProviderId, out var defaultModelId))
        {
            errors.Add($"defaultModel '{Config.DefaultModel}' is invalid; expected '<provider>/<model>'.");
            return new AgentConfigValidationResult(errors, warnings);
        }

        if (!providersById.TryGetValue(defaultProviderId, out var defaultProvider))
        {
            errors.Add($"defaultModel provider '{defaultProviderId}' is not configured.");
            return new AgentConfigValidationResult(errors, warnings);
        }

        var defaultProviderModelExists = defaultProvider.Models.Any(model =>
            model.Id.Equals(defaultModelId, StringComparison.OrdinalIgnoreCase));
        if (!defaultProviderModelExists)
        {
            errors.Add(
                $"defaultModel '{Config.DefaultModel}' points to missing model '{defaultModelId}' under provider '{defaultProviderId}'.");
        }

        if (string.IsNullOrWhiteSpace(ResolveProviderApiKey(defaultProvider)))
        {
            errors.Add(
                $"Missing API key for defaultModel provider '{defaultProviderId}'. " +
                $"Configure providers[].apiKey or set one of: {string.Join(", ", GetProviderApiKeyEnvironmentVariableCandidates(defaultProvider))}.");
        }

        return new AgentConfigValidationResult(errors, warnings);
    }

    public void SaveToFile(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(Config, JsonOptions);
        File.WriteAllText(path, json);
    }

    public AgentRuntimeOptions BuildRuntimeOptions(
        string? modelString = null,
        string? workingDirectory = null,
        string? sessionDirectory = null,
        string? agentDirectory = null,
        ThinkingLevel thinkingLevel = ThinkingLevel.Off,
        string? systemPrompt = null,
        string? appendSystemPrompt = null,
        bool discoverSystemPromptFile = true,
        bool includeProjectContextFiles = true,
        bool enableSkills = true,
        bool includeDefaultSkills = true,
        IReadOnlyList<string>? skillPaths = null,
        int maxTurns = 20,
        bool discoverExtensions = true,
        IReadOnlyList<string>? extensionPaths = null,
        Action<string>? onDebugLog = null)
    {
        modelString ??= Config.DefaultModel;

        var (providerId, modelId) = ParseModelString(modelString);
        var provider = Config.Providers.FirstOrDefault(p =>
            p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Provider '{providerId}' is not configured");

        var model = provider.Models.FirstOrDefault(m =>
            m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Model '{modelId}' is not configured under provider '{providerId}'");

        // Prefer provider-level API format, keep legacy model-level override for compatibility.
        var apiFormat = model.Api ?? provider.Api;

        var apiKey = ResolveProviderApiKey(provider);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Missing API key for provider '{providerId}'. Configure providers[].apiKey or set one of: " +
                $"{string.Join(", ", GetProviderApiKeyEnvironmentVariableCandidates(provider))}.");
        }

        var baseUrl = ResolveProviderBaseUrl(provider);

        return new AgentRuntimeOptions
        {
            Model = new ModelDescriptor(
                ProviderId: provider.Id,
                ModelId: model.Id,
                ApiKind: AgentConfig.ToProviderApiKind(apiFormat),
                ContextWindow: model.ContextWindow,
                MaxOutputTokens: model.MaxOutputTokens),
            ApiKey = apiKey,
            BaseUrl = baseUrl,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            SessionDirectory = sessionDirectory ?? DefaultSessionDirectory(),
            AgentDirectory = agentDirectory ?? DefaultAgentDirectory(),
            ThinkingLevel = thinkingLevel,
            SystemPrompt = systemPrompt ??
                           "You are a coding agent. Work precisely, call tools when needed, and verify intermediate results.",
            AppendSystemPrompt = appendSystemPrompt,
            DiscoverSystemPromptFile = discoverSystemPromptFile && string.IsNullOrWhiteSpace(systemPrompt),
            IncludeProjectContextFiles = includeProjectContextFiles,
            EnableSkills = enableSkills,
            IncludeDefaultSkills = includeDefaultSkills,
            SkillPaths = skillPaths,
            DiscoverExtensions = discoverExtensions,
            ExtensionPaths = extensionPaths,
            OnDebugLog = onDebugLog,
            MaxTurns = maxTurns
        };
    }

    public IReadOnlyList<string> GetAvailableModels()
    {
        return Config.Providers
            .SelectMany(provider => provider.Models.Select(model => $"{provider.Id}/{model.Id}"))
            .ToList();
    }

    public static (string ProviderId, string ModelId) ParseModelString(string modelString)
    {
        var parts = modelString.Split('/', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new ArgumentException($"Invalid model string '{modelString}', expected '<provider>/<model>'");

        return (parts[0], parts[1]);
    }

    public static string DefaultConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Sharp", "config.json");
    }

    public static string DefaultSessionDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".sharp", "sessions");
    }

    public static string DefaultAgentDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".sharp");
    }

    private static AgentConfig ApplyEnvironmentOverrides(AgentConfig config)
    {
        foreach (var provider in config.Providers)
        {
            provider.ApiKey = ResolveProviderApiKey(provider);
            provider.BaseUrl = ResolveProviderBaseUrl(provider);
        }

        var defaultModel = Environment.GetEnvironmentVariable("LLM_DEFAULT_MODEL");
        if (!string.IsNullOrWhiteSpace(defaultModel))
            config.DefaultModel = defaultModel;

        return config;
    }

    private static bool TryParseModelString(string modelString, out string providerId, out string modelId)
    {
        providerId = string.Empty;
        modelId = string.Empty;

        if (string.IsNullOrWhiteSpace(modelString))
            return false;

        var parts = modelString.Split('/', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        providerId = parts[0];
        modelId = parts[1];
        return !string.IsNullOrWhiteSpace(providerId) && !string.IsNullOrWhiteSpace(modelId);
    }

    private static string ResolveProviderBaseUrl(ProviderConfig provider)
        => ResolveProviderValue(provider.BaseUrl, GetProviderBaseUrlEnvironmentVariableCandidates(provider));

    private static string? ResolveProviderApiKey(ProviderConfig provider)
        => ResolveProviderValue(provider.ApiKey, GetProviderApiKeyEnvironmentVariableCandidates(provider));

    private static string ResolveProviderValue(
        string? configuredValue,
        IReadOnlyList<string> environmentVariableCandidates)
    {
        foreach (var environmentVariable in environmentVariableCandidates)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return configuredValue ?? string.Empty;
    }

    private static IReadOnlyList<string> GetProviderApiKeyEnvironmentVariableCandidates(ProviderConfig provider)
        => BuildProviderEnvironmentVariableCandidates(provider, "API_KEY");

    private static IReadOnlyList<string> GetProviderBaseUrlEnvironmentVariableCandidates(ProviderConfig provider)
        => BuildProviderEnvironmentVariableCandidates(provider, "BASE_URL");

    private static IReadOnlyList<string> BuildProviderEnvironmentVariableCandidates(ProviderConfig provider, string suffix)
    {
        var candidates = new List<string>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string value)
        {
            if (unique.Add(value))
                candidates.Add(value);
        }

        var providerToken = NormalizeProviderIdForEnvironment(provider.Id);
        Add($"SHARP_{providerToken}_{suffix}");
        Add($"{providerToken}_{suffix}");

        if (provider.Api == ModelApiFormat.OpenAiCompletions
            || provider.Id.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
            Add($"OPENAI_{suffix}");
        }

        if (provider.Api == ModelApiFormat.AnthropicMessages
            || provider.Id.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
        {
            Add($"ANTHROPIC_{suffix}");
        }

        return candidates;
    }

    private static string NormalizeProviderIdForEnvironment(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return "PROVIDER";

        var builder = new StringBuilder(providerId.Length);
        var previousUnderscore = false;
        foreach (var ch in providerId)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToUpperInvariant(ch));
                previousUnderscore = false;
                continue;
            }

            if (!previousUnderscore)
            {
                builder.Append('_');
                previousUnderscore = true;
            }
        }

        var normalized = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "PROVIDER" : normalized;
    }
}
