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

    public static AgentConfigurationService LoadFromFile(string? path = null)
    {
        // If a specific path is provided, try only that
        if (!string.IsNullOrWhiteSpace(path))
        {
            return TryLoadFromPath(path) ?? new AgentConfigurationService(ApplyEnvironmentOverrides(new AgentConfig()));
        }

        // Try CWD first, then agent dir
        var cwdConfig = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
        var agentDirConfig = Path.Combine(DefaultAgentDirectory(), "config.json");

        // Try CWD
        var result = TryLoadFromPath(cwdConfig);
        if (result != null) return result;

        // Try agent directory
        result = TryLoadFromPath(agentDirConfig);
        if (result != null) return result;

        // Fall back to default empty config
        return new AgentConfigurationService(ApplyEnvironmentOverrides(new AgentConfig()));
    }

    private static AgentConfigurationService? TryLoadFromPath(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<AgentConfig>(json, JsonOptions);
            if (config != null)
            {
                config.Providers = ProviderMerger.Merge(
                    AgentConfig.GetBuiltInProviders(),
                    config.Providers);
                return new AgentConfigurationService(ApplyEnvironmentOverrides(config));
            }
        }
        catch
        {
            // Config file exists but is invalid - will try next location
        }

        return null;
    }

    public AgentConfigValidationResult ValidateConfig(string? agentDirectory = null)
    {
        var resolvedAgentDirectory = agentDirectory ?? DefaultAgentDirectory();
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

        if (string.IsNullOrWhiteSpace(ResolveProviderApiKey(defaultProvider, resolvedAgentDirectory)))
        {
            errors.Add(
                $"Missing API key for defaultModel provider '{defaultProviderId}'. " +
                $"{BuildCredentialGuidance(defaultProvider, resolvedAgentDirectory)}");
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
        var resolvedAgentDirectory = agentDirectory ?? DefaultAgentDirectory();

        var (providerId, modelId) = ParseModelString(modelString);
        var provider = Config.Providers.FirstOrDefault(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
            throw new InvalidOperationException($"Provider '{providerId}' is not configured");
        var model = provider.Models.FirstOrDefault(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        if (model is null)
            throw new InvalidOperationException($"Model '{modelId}' is not configured under provider '{providerId}'");

        // Prefer provider-level API format, keep legacy model-level override for compatibility.
        var apiFormat = model.Api ?? provider.Api;

        var apiKey = ResolveProviderApiKey(provider, resolvedAgentDirectory);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Missing API key for provider '{providerId}'. {BuildCredentialGuidance(provider, resolvedAgentDirectory)}");
        }

        var baseUrl = ResolveProviderBaseUrl(provider);
        var providerApiKind = AgentConfig.ToProviderApiKind(apiFormat);
        var modelCapabilities = AgentConfig.ToModelCapabilities(apiFormat, model.Capabilities);
        var modelPricing = AgentConfig.ToModelPricing(model.Pricing);
        var credentialCandidates = GetProviderCredentialEnvironmentVariableCandidates(provider);
        ILlmBearerTokenSource tokenSource = provider.Id.Equals("google-antigravity", StringComparison.OrdinalIgnoreCase)
            ? new AntigravityBearerTokenSource(credentialCandidates, fallbackToken: apiKey)
            : new EnvironmentVariableBearerTokenSource(credentialCandidates, fallbackToken: apiKey);

        var mergedHeaders = MergeHeaders(provider.Headers, model.Headers);
        var resolvedHeaders = ConfigValueResolver.ResolveHeaders(mergedHeaders);

        if (provider.AuthHeader == true && !string.IsNullOrWhiteSpace(apiKey))
        {
            resolvedHeaders = AddAuthorizationHeader(resolvedHeaders, apiKey);
        }

        return new AgentRuntimeOptions
        {
            Model = new ModelDescriptor(
                ProviderId: provider.Id,
                ModelId: model.Id,
                ApiKind: providerApiKind,
                ContextWindow: model.ContextWindow,
                MaxOutputTokens: model.MaxOutputTokens,
                OpenAiCompletionsCompat: apiFormat == ModelApiFormat.OpenAiCompletions
                    ? OpenAiCompatResolver.ResolveCompletionsCompat(provider.Id, baseUrl, model.Compat)
                    : null,
                Capabilities: modelCapabilities,
                Pricing: modelPricing,
                DisplayName: model.Name ?? model.Id,
                Headers: resolvedHeaders),
            ApiKey = apiKey,
            CredentialProvider = new CachingBearerCredentialProvider(
                providerApiKind,
                tokenSource,
                cacheTokensWithoutExpiry: false),
            BaseUrl = baseUrl,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            SessionDirectory = sessionDirectory ?? DefaultSessionDirectory(),
            AgentDirectory = resolvedAgentDirectory,
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
        => Config.Providers
            .SelectMany(p => p.Models.Select(m => $"{p.Id}/{m.Id}"))
            .ToList();

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

    public static string DefaultAuthStorePath(string? agentDirectory = null)
    {
        var resolvedAgentDirectory = string.IsNullOrWhiteSpace(agentDirectory)
            ? DefaultAgentDirectory()
            : agentDirectory!;
        return Path.Combine(resolvedAgentDirectory, "auth.json");
    }

    private static AgentConfig ApplyEnvironmentOverrides(AgentConfig config)
    {
        foreach (var provider in config.Providers)
        {
            provider.ApiKey = ResolveProviderApiKeyFromEnvironmentOrConfig(provider);
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

    private static string? ResolveProviderApiKey(ProviderConfig provider, string agentDirectory)
    {
        // 1. Environment variables
        var fromEnvironment = ResolveProviderValue(string.Empty, GetProviderCredentialEnvironmentVariableCandidates(provider));
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        // 2-3. AuthStore (api_key resolved + oauth access token)
        var authStorePath = DefaultAuthStorePath(agentDirectory);
        var authStore = AuthStore.LoadFromFile(authStorePath);
        var fromAuthStore = authStore.GetApiKey(provider.Id);
        if (!string.IsNullOrWhiteSpace(fromAuthStore))
            return fromAuthStore;

        // Legacy OAuthCredentialStore fallback
        var fromOAuthStore = ResolveProviderApiKeyFromOAuthStore(provider, agentDirectory);
        if (!string.IsNullOrWhiteSpace(fromOAuthStore))
            return fromOAuthStore;

        // 4. Config provider.ApiKey (resolved via ConfigValueResolver)
        var resolvedConfigKey = ConfigValueResolver.Resolve(provider.ApiKey);
        if (!string.IsNullOrWhiteSpace(resolvedConfigKey))
            return resolvedConfigKey;

        return string.Empty;
    }

    private static string? ResolveProviderApiKeyFromEnvironmentOrConfig(ProviderConfig provider)
        => ResolveProviderValue(provider.ApiKey, GetProviderCredentialEnvironmentVariableCandidates(provider));

    private static string? ResolveProviderApiKeyFromOAuthStore(ProviderConfig provider, string agentDirectory)
    {
        if (!IsOAuthProvider(provider))
            return null;

        var authStorePath = DefaultAuthStorePath(agentDirectory);
        var authStore = OAuthCredentialStore.LoadFromFile(authStorePath);
        return authStore.TryGetCredential(provider.Id, out var credential) ? credential : null;
    }

    private static IReadOnlyList<string> GetProviderCredentialEnvironmentVariableCandidates(ProviderConfig provider)
    {
        var candidates = new List<string>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRange(IReadOnlyList<string> values)
        {
            foreach (var value in values)
            {
                if (unique.Add(value))
                    candidates.Add(value);
            }
        }

        AddRange(GetProviderApiKeyEnvironmentVariableCandidates(provider));
        AddRange(BuildProviderEnvironmentVariableCandidates(provider, "ACCESS_TOKEN"));
        AddRange(BuildProviderEnvironmentVariableCandidates(provider, "OAUTH_TOKEN"));

        return candidates;
    }

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

        if (provider.Id.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
            Add($"OPENAI_{suffix}");
        }

        if (provider.Id.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
        {
            Add($"ANTHROPIC_{suffix}");
        }

        if (provider.Id.Equals("kimi-coding", StringComparison.OrdinalIgnoreCase))
        {
            Add($"KIMI_{suffix}");
        }

        if (provider.Id.Equals("google-antigravity", StringComparison.OrdinalIgnoreCase))
        {
            Add($"ANTIGRAVITY_{suffix}");
        }

        if (provider.Id.Equals("huggingface", StringComparison.OrdinalIgnoreCase) &&
            suffix.Equals("API_KEY", StringComparison.Ordinal))
        {
            Add("HF_TOKEN");
        }

        if (provider.Id.Equals("github-copilot", StringComparison.OrdinalIgnoreCase) &&
            suffix.Equals("API_KEY", StringComparison.Ordinal))
        {
            Add("COPILOT_GITHUB_TOKEN");
            Add("GH_TOKEN");
            Add("GITHUB_TOKEN");
        }

        if (provider.Id.Equals("google", StringComparison.OrdinalIgnoreCase) &&
            suffix.Equals("API_KEY", StringComparison.Ordinal))
        {
            Add("GEMINI_API_KEY");
        }

        if (provider.Id.Equals("azure-openai-responses", StringComparison.OrdinalIgnoreCase) &&
            suffix.Equals("API_KEY", StringComparison.Ordinal))
        {
            Add("AZURE_OPENAI_API_KEY");
        }

        if (provider.Id.Equals("xai", StringComparison.OrdinalIgnoreCase) &&
            suffix.Equals("API_KEY", StringComparison.Ordinal))
        {
            Add("XAI_API_KEY");
        }

        if (provider.Id.Equals("groq", StringComparison.OrdinalIgnoreCase) &&
            suffix.Equals("API_KEY", StringComparison.Ordinal))
        {
            Add("GROQ_API_KEY");
        }

        if (provider.Id.Equals("cerebras", StringComparison.OrdinalIgnoreCase) &&
            suffix.Equals("API_KEY", StringComparison.Ordinal))
        {
            Add("CEREBRAS_API_KEY");
        }

        if (provider.Id.Equals("openrouter", StringComparison.OrdinalIgnoreCase) &&
            suffix.Equals("API_KEY", StringComparison.Ordinal))
        {
            Add("OPENROUTER_API_KEY");
        }

        if (provider.Id.Equals("vercel-ai-gateway", StringComparison.OrdinalIgnoreCase) &&
            suffix.Equals("API_KEY", StringComparison.Ordinal))
        {
            Add("AI_GATEWAY_API_KEY");
        }

        if (provider.Id.Equals("zai", StringComparison.OrdinalIgnoreCase) &&
            suffix.Equals("API_KEY", StringComparison.Ordinal))
        {
            Add("ZAI_API_KEY");
        }

        if (provider.Id.Equals("mistral", StringComparison.OrdinalIgnoreCase) &&
            suffix.Equals("API_KEY", StringComparison.Ordinal))
        {
            Add("MISTRAL_API_KEY");
        }

        if (provider.Id.Equals("minimax", StringComparison.OrdinalIgnoreCase) &&
            suffix.Equals("API_KEY", StringComparison.Ordinal))
        {
            Add("MINIMAX_API_KEY");
        }

        if (provider.Id.Equals("minimax-cn", StringComparison.OrdinalIgnoreCase) &&
            suffix.Equals("API_KEY", StringComparison.Ordinal))
        {
            Add("MINIMAX_CN_API_KEY");
        }

        if (provider.Id.Equals("opencode", StringComparison.OrdinalIgnoreCase) &&
            suffix.Equals("API_KEY", StringComparison.Ordinal))
        {
            Add("OPENCODE_API_KEY");
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

    private static IReadOnlyDictionary<string, string>? MergeHeaders(
        Dictionary<string, string>? providerHeaders,
        Dictionary<string, string>? modelHeaders)
    {
        if (providerHeaders is not { Count: > 0 } && modelHeaders is not { Count: > 0 })
            return null;

        var merged = new Dictionary<string, string>(StringComparer.Ordinal);

        if (providerHeaders is { Count: > 0 })
        {
            foreach (var (key, value) in providerHeaders)
                merged[key] = value;
        }

        if (modelHeaders is { Count: > 0 })
        {
            foreach (var (key, value) in modelHeaders)
                merged[key] = value;
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string> AddAuthorizationHeader(
        IReadOnlyDictionary<string, string>? existing,
        string apiKey)
    {
        var result = existing is not null
            ? new Dictionary<string, string>(existing, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        result.TryAdd("Authorization", $"Bearer {apiKey}");
        return result;
    }

    private static bool IsOAuthProvider(ProviderConfig provider)
        => provider.Id.Equals("google-antigravity", StringComparison.OrdinalIgnoreCase);

    private static bool IsOAuthProvider(ProviderConfig provider, string agentDirectory)
    {
        if (provider.Id.Equals("google-antigravity", StringComparison.OrdinalIgnoreCase))
            return true;

        var authStore = AuthStore.LoadFromFile(DefaultAuthStorePath(agentDirectory));
        var cred = authStore.Get(provider.Id);
        return cred is OAuthAuthCredential;
    }

    private static string BuildCredentialGuidance(ProviderConfig provider, string agentDirectory)
    {
        var envCandidates = string.Join(", ", GetProviderCredentialEnvironmentVariableCandidates(provider));
        var authPath = DefaultAuthStorePath(agentDirectory);

        if (IsOAuthProvider(provider, agentDirectory))
        {
            return $"Run OAuth login, add api_key entry in '{authPath}', " +
                   $"configure providers[].apiKey, or set one of: {envCandidates}.";
        }

        return $"Add entry in '{authPath}', configure providers[].apiKey, or set one of: {envCandidates}.";
    }
}
