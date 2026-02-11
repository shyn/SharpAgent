using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text.Json;
using Sharp.AI;
using Sharp.Core.Configuration;

namespace Sharp.Gui.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions ConfigJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly Action<string> _onApply;
    private readonly AntigravityOAuthLoginService _antigravityOAuthLoginService = new();

    public ObservableCollection<SettingsMenuItemViewModel> MenuItems { get; } =
    [
        new("general", "General"),
        new("providers", "Providers")
    ];

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(LoginSelectedProviderCommand))]
    private string _configFilePath;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralSectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsProvidersSectionSelected))]
    private SettingsMenuItemViewModel? _selectedMenuItem;

    [ObservableProperty]
    private ObservableCollection<ProviderEntryViewModel> _providers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedProvider))]
    [NotifyCanExecuteChangedFor(nameof(LoginSelectedProviderCommand))]
    private ProviderEntryViewModel? _selectedProvider;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginSelectedProviderCommand))]
    private bool _isLoggingIn;

    /// <summary>
    /// Set by the View code-behind to provide file picker integration.
    /// </summary>
    public Func<Task<string?>>? BrowseFileAsync { get; set; }

    public bool IsGeneralSectionSelected => SelectedMenuItem?.Key == "general";
    public bool IsProvidersSectionSelected => SelectedMenuItem?.Key == "providers";
    public bool HasSelectedProvider => SelectedProvider != null;

    public SettingsViewModel(string currentPath, Action<string> onApply)
    {
        _configFilePath = currentPath;
        _onApply = onApply;
        SelectedMenuItem = MenuItems.FirstOrDefault();
        ReloadProviders(preserveSelection: false);
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (BrowseFileAsync == null) return;

        var path = await BrowseFileAsync();
        if (!string.IsNullOrWhiteSpace(path))
            ConfigFilePath = path;
    }

    [RelayCommand]
    private void Apply()
    {
        if (string.IsNullOrWhiteSpace(ConfigFilePath))
        {
            StatusMessage = "Path cannot be empty.";
            IsError = true;
            return;
        }

        if (!File.Exists(ConfigFilePath))
        {
            StatusMessage = $"File not found: {ConfigFilePath}";
            IsError = true;
            return;
        }

        StatusMessage = "Applied. Reloading...";
        IsError = false;
        _onApply(ConfigFilePath);
    }

    [RelayCommand]
    private void ResetToDefault()
    {
        // Prefer agent directory for settings
        var agentDir = Sharp.Core.Configuration.AgentConfigurationService.DefaultAgentDirectory();
        ConfigFilePath = Path.Combine(agentDir, "config.json");
        StatusMessage = "Reset to agent directory config path.";
        IsError = false;
    }

    [RelayCommand]
    private void RefreshProviders()
    {
        ReloadProviders(preserveSelection: true);
    }

    private bool CanLoginSelectedProvider()
        => !IsLoggingIn && SelectedProvider is { IsOAuthProvider: true, HasCredential: false };

    [RelayCommand(CanExecute = nameof(CanLoginSelectedProvider))]
    private async Task LoginSelectedProviderAsync()
    {
        if (SelectedProvider == null)
            return;

        if (!SelectedProvider.Id.Equals("google-antigravity", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = $"OAuth login is not supported for provider '{SelectedProvider.Id}' in this UI.";
            IsError = true;
            return;
        }

        try
        {
            IsLoggingIn = true;
            IsError = false;
            StatusMessage = "Starting OAuth login flow...";

            var progress = new Progress<string>(message =>
            {
                StatusMessage = $"[oauth] {message}";
                IsError = false;
            });

            var credential = await _antigravityOAuthLoginService.LoginAsync(progress);
            SaveProviderOAuthCredential(SelectedProvider, credential);

            ReloadProviders(preserveSelection: true);
            StatusMessage = $"OAuth login succeeded for '{SelectedProvider.Id}'. Credential saved to config.";
            IsError = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"OAuth login failed: {ex.Message}";
            IsError = true;
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    partial void OnConfigFilePathChanged(string value)
    {
        ReloadProviders(preserveSelection: true);
    }

    private void ReloadProviders(bool preserveSelection)
    {
        var previousSelectedProviderId = preserveSelection ? SelectedProvider?.Id : null;
        var authStorePath = AgentConfigurationService.DefaultAuthStorePath();
        var authStore = OAuthCredentialStore.LoadFromFile(authStorePath);

        var config = LoadRawConfig(out var loadError);
        if (!string.IsNullOrWhiteSpace(loadError))
        {
            StatusMessage = $"Failed to parse config file: {loadError}";
            IsError = true;
        }

        var builtInProviders = new AgentConfig().Providers;
        var configProviders = config.Providers
            .Where(static provider => !string.IsNullOrWhiteSpace(provider.Id))
            .ToDictionary(static provider => provider.Id, CloneProvider, StringComparer.OrdinalIgnoreCase);
        var builtInProviderIds = new HashSet<string>(
            builtInProviders.Select(static provider => provider.Id),
            StringComparer.OrdinalIgnoreCase);

        var entries = new List<ProviderEntryViewModel>();

        foreach (var builtIn in builtInProviders)
        {
            if (configProviders.TryGetValue(builtIn.Id, out var configured))
            {
                entries.Add(CreateProviderEntry(configured, ProviderSource.BuiltInAndConfig, authStore, authStorePath));
            }
            else
            {
                entries.Add(CreateProviderEntry(CloneProvider(builtIn), ProviderSource.BuiltIn, authStore, authStorePath));
            }
        }

        foreach (var configured in config.Providers.Where(provider =>
                     !string.IsNullOrWhiteSpace(provider.Id) && !builtInProviderIds.Contains(provider.Id)))
        {
            entries.Add(CreateProviderEntry(CloneProvider(configured), ProviderSource.ConfigFile, authStore, authStorePath));
        }

        Providers = new ObservableCollection<ProviderEntryViewModel>(entries);

        if (!string.IsNullOrWhiteSpace(previousSelectedProviderId))
        {
            SelectedProvider = Providers.FirstOrDefault(provider =>
                provider.Id.Equals(previousSelectedProviderId, StringComparison.OrdinalIgnoreCase));
        }

        SelectedProvider ??= Providers.FirstOrDefault();
    }

    private ProviderEntryViewModel CreateProviderEntry(
        ProviderConfig provider,
        ProviderSource source,
        OAuthCredentialStore authStore,
        string authStorePath)
    {
        var isOAuthProvider = provider.Id.Equals("google-antigravity", StringComparison.OrdinalIgnoreCase);
        var credentialEnvVar = ResolveCredentialEnvironmentVariable(provider.Id);
        var hasEnvCredential = !string.IsNullOrWhiteSpace(credentialEnvVar);
        var hasAuthStoreCredential = isOAuthProvider && authStore.TryGetCredential(provider.Id, out _);
        var hasConfigCredential = !string.IsNullOrWhiteSpace(provider.ApiKey);
        var hasCredential = hasEnvCredential || hasAuthStoreCredential || hasConfigCredential;

        var credentialStatus = hasEnvCredential
            ? $"Configured via environment variable '{credentialEnvVar}'."
            : hasAuthStoreCredential
                ? $"Configured in auth store '{authStorePath}'."
            : hasConfigCredential
                ? isOAuthProvider
                    ? "Configured in config file (legacy). Prefer OAuth auth store."
                    : "Configured in config file."
                : "No credential configured.";

        var sourceLabel = source switch
        {
            ProviderSource.BuiltIn => "Built-in",
            ProviderSource.ConfigFile => "Config",
            ProviderSource.BuiltInAndConfig => "Built-in + Config",
            _ => "Unknown"
        };

        return new ProviderEntryViewModel(
            provider,
            sourceLabel,
            isOAuthProvider,
            hasCredential,
            credentialStatus);
    }

    private void SaveProviderOAuthCredential(
        ProviderEntryViewModel providerEntry,
        AntigravityOAuthCredential credential)
    {
        var authStorePath = AgentConfigurationService.DefaultAuthStorePath();
        var authStore = OAuthCredentialStore.LoadFromFile(authStorePath);
        authStore.SetCredential(
            providerEntry.Id,
            AntigravityOAuthLoginService.ToCredentialEnvelope(credential));
        authStore.SaveToFile(authStorePath);
    }

    private AgentConfig LoadRawConfig(out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(ConfigFilePath))
            return CreateEmptyConfig();

        if (!File.Exists(ConfigFilePath))
            return CreateEmptyConfig();

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<AgentConfig>(json, ConfigJsonOptions) ?? CreateEmptyConfig();
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return CreateEmptyConfig();
        }
    }

    private static AgentConfig CreateEmptyConfig()
        => new() { Providers = [] };

    private static string? ResolveCredentialEnvironmentVariable(string providerId)
    {
        foreach (var candidate in BuildCredentialEnvironmentVariableCandidates(providerId))
        {
            var value = Environment.GetEnvironmentVariable(candidate);
            if (!string.IsNullOrWhiteSpace(value))
                return candidate;
        }

        return null;
    }

    private static IReadOnlyList<string> BuildCredentialEnvironmentVariableCandidates(string providerId)
    {
        var candidates = new List<string>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string value)
        {
            if (unique.Add(value))
                candidates.Add(value);
        }

        var providerToken = NormalizeProviderIdForEnvironment(providerId);
        Add($"SHARP_{providerToken}_API_KEY");
        Add($"{providerToken}_API_KEY");
        Add($"SHARP_{providerToken}_ACCESS_TOKEN");
        Add($"{providerToken}_ACCESS_TOKEN");
        Add($"SHARP_{providerToken}_OAUTH_TOKEN");
        Add($"{providerToken}_OAUTH_TOKEN");

        if (providerId.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
            Add("OPENAI_API_KEY");
            Add("OPENAI_ACCESS_TOKEN");
            Add("OPENAI_OAUTH_TOKEN");
        }

        if (providerId.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
        {
            Add("ANTHROPIC_API_KEY");
            Add("ANTHROPIC_ACCESS_TOKEN");
            Add("ANTHROPIC_OAUTH_TOKEN");
        }

        if (providerId.Equals("kimi-coding", StringComparison.OrdinalIgnoreCase))
        {
            Add("KIMI_API_KEY");
            Add("KIMI_ACCESS_TOKEN");
            Add("KIMI_OAUTH_TOKEN");
        }

        if (providerId.Equals("google-antigravity", StringComparison.OrdinalIgnoreCase))
        {
            Add("ANTIGRAVITY_ACCESS_TOKEN");
            Add("ANTIGRAVITY_OAUTH_TOKEN");
            Add("ANTIGRAVITY_API_KEY");
        }

        if (providerId.Equals("huggingface", StringComparison.OrdinalIgnoreCase))
            Add("HF_TOKEN");

        if (providerId.Equals("github-copilot", StringComparison.OrdinalIgnoreCase))
        {
            Add("COPILOT_GITHUB_TOKEN");
            Add("GH_TOKEN");
            Add("GITHUB_TOKEN");
        }

        return candidates;
    }

    private static string NormalizeProviderIdForEnvironment(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return "PROVIDER";

        var builder = new System.Text.StringBuilder(providerId.Length);
        var previousUnderscore = false;
        foreach (var ch in providerId)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToUpperInvariant(ch));
                previousUnderscore = false;
            }
            else if (!previousUnderscore)
            {
                builder.Append('_');
                previousUnderscore = true;
            }
        }

        var normalized = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "PROVIDER" : normalized;
    }

    private static ProviderConfig CloneProvider(ProviderConfig source)
    {
        return new ProviderConfig
        {
            Id = source.Id,
            Api = source.Api,
            ApiKey = source.ApiKey,
            BaseUrl = source.BaseUrl,
            Models = source.Models.Select(CloneModel).ToList()
        };
    }

    private static ModelConfig CloneModel(ModelConfig source)
    {
        return new ModelConfig
        {
            Id = source.Id,
            Api = source.Api,
            ContextWindow = source.ContextWindow,
            MaxOutputTokens = source.MaxOutputTokens,
            Compat = source.Compat == null
                ? null
                : new OpenAiCompletionsCompatConfig
                {
                    SupportsStore = source.Compat.SupportsStore,
                    SupportsDeveloperRole = source.Compat.SupportsDeveloperRole,
                    SupportsReasoningEffort = source.Compat.SupportsReasoningEffort,
                    SupportsUsageInStreaming = source.Compat.SupportsUsageInStreaming,
                    SupportsStrictMode = source.Compat.SupportsStrictMode,
                    RequiresToolResultName = source.Compat.RequiresToolResultName,
                    RequiresAssistantAfterToolResult = source.Compat.RequiresAssistantAfterToolResult,
                    RequiresMistralToolIds = source.Compat.RequiresMistralToolIds,
                    RequiresThinkingAsText = source.Compat.RequiresThinkingAsText,
                    MaxTokensField = source.Compat.MaxTokensField,
                    ThinkingFormat = source.Compat.ThinkingFormat,
                    OpenRouterRouting = source.Compat.OpenRouterRouting == null
                        ? null
                        : new OpenAiRoutingPreferences(
                            source.Compat.OpenRouterRouting.Only?.ToArray(),
                            source.Compat.OpenRouterRouting.Order?.ToArray()),
                    VercelGatewayRouting = source.Compat.VercelGatewayRouting == null
                        ? null
                        : new OpenAiRoutingPreferences(
                            source.Compat.VercelGatewayRouting.Only?.ToArray(),
                            source.Compat.VercelGatewayRouting.Order?.ToArray())
                },
            Capabilities = source.Capabilities == null
                ? null
                : new ModelCapabilitiesConfig
                {
                    SupportsReasoning = source.Capabilities.SupportsReasoning,
                    SupportsImageInput = source.Capabilities.SupportsImageInput,
                    SupportsToolCall = source.Capabilities.SupportsToolCall
                },
            Pricing = source.Pricing == null
                ? null
                : new ModelPricingConfig
                {
                    InputPerMillionTokens = source.Pricing.InputPerMillionTokens,
                    OutputPerMillionTokens = source.Pricing.OutputPerMillionTokens,
                    CacheReadPerMillionTokens = source.Pricing.CacheReadPerMillionTokens,
                    CacheWritePerMillionTokens = source.Pricing.CacheWritePerMillionTokens
                }
        };
    }

    private enum ProviderSource
    {
        BuiltIn,
        ConfigFile,
        BuiltInAndConfig
    }
}

public sealed class SettingsMenuItemViewModel
{
    public SettingsMenuItemViewModel(string key, string title)
    {
        Key = key;
        Title = title;
    }

    public string Key { get; }
    public string Title { get; }
}

public sealed class ProviderEntryViewModel
{
    private readonly ProviderConfig _providerConfig;

    public ProviderEntryViewModel(
        ProviderConfig providerConfig,
        string sourceLabel,
        bool isOAuthProvider,
        bool hasCredential,
        string credentialStatus)
    {
        _providerConfig = CloneProvider(providerConfig);
        SourceLabel = sourceLabel;
        IsOAuthProvider = isOAuthProvider;
        HasCredential = hasCredential;
        CredentialStatus = credentialStatus;

        Id = _providerConfig.Id;
        Api = _providerConfig.Api switch
        {
            ModelApiFormat.OpenAiCompletions => "openai-completions",
            ModelApiFormat.OpenAiResponses => "openai-responses",
            ModelApiFormat.AnthropicMessages => "anthropic-messages",
            ModelApiFormat.GoogleGeminiCli => "google-gemini-cli",
            _ => _providerConfig.Api.ToString()
        };
        BaseUrl = string.IsNullOrWhiteSpace(_providerConfig.BaseUrl) ? "(not set)" : _providerConfig.BaseUrl;
        Models = _providerConfig.Models
            .Select(static model => new ProviderModelViewModel(model.Id, model.ContextWindow, model.MaxOutputTokens))
            .ToList();
    }

    public string Id { get; }
    public string SourceLabel { get; }
    public string Api { get; }
    public string BaseUrl { get; }
    public bool IsOAuthProvider { get; }
    public bool HasCredential { get; }
    public string CredentialStatus { get; }
    public IReadOnlyList<ProviderModelViewModel> Models { get; }
    public bool NeedsLogin => IsOAuthProvider && !HasCredential;

    public ProviderConfig ToProviderConfig() => CloneProvider(_providerConfig);

    private static ProviderConfig CloneProvider(ProviderConfig source)
    {
        return new ProviderConfig
        {
            Id = source.Id,
            Api = source.Api,
            ApiKey = source.ApiKey,
            BaseUrl = source.BaseUrl,
            Models = source.Models.Select(CloneModel).ToList()
        };
    }

    private static ModelConfig CloneModel(ModelConfig source)
    {
        return new ModelConfig
        {
            Id = source.Id,
            Api = source.Api,
            ContextWindow = source.ContextWindow,
            MaxOutputTokens = source.MaxOutputTokens,
            Compat = source.Compat == null
                ? null
                : new OpenAiCompletionsCompatConfig
                {
                    SupportsStore = source.Compat.SupportsStore,
                    SupportsDeveloperRole = source.Compat.SupportsDeveloperRole,
                    SupportsReasoningEffort = source.Compat.SupportsReasoningEffort,
                    SupportsUsageInStreaming = source.Compat.SupportsUsageInStreaming,
                    SupportsStrictMode = source.Compat.SupportsStrictMode,
                    RequiresToolResultName = source.Compat.RequiresToolResultName,
                    RequiresAssistantAfterToolResult = source.Compat.RequiresAssistantAfterToolResult,
                    RequiresMistralToolIds = source.Compat.RequiresMistralToolIds,
                    RequiresThinkingAsText = source.Compat.RequiresThinkingAsText,
                    MaxTokensField = source.Compat.MaxTokensField,
                    ThinkingFormat = source.Compat.ThinkingFormat,
                    OpenRouterRouting = source.Compat.OpenRouterRouting == null
                        ? null
                        : new OpenAiRoutingPreferences(
                            source.Compat.OpenRouterRouting.Only?.ToArray(),
                            source.Compat.OpenRouterRouting.Order?.ToArray()),
                    VercelGatewayRouting = source.Compat.VercelGatewayRouting == null
                        ? null
                        : new OpenAiRoutingPreferences(
                            source.Compat.VercelGatewayRouting.Only?.ToArray(),
                            source.Compat.VercelGatewayRouting.Order?.ToArray())
                },
            Capabilities = source.Capabilities == null
                ? null
                : new ModelCapabilitiesConfig
                {
                    SupportsReasoning = source.Capabilities.SupportsReasoning,
                    SupportsImageInput = source.Capabilities.SupportsImageInput,
                    SupportsToolCall = source.Capabilities.SupportsToolCall
                },
            Pricing = source.Pricing == null
                ? null
                : new ModelPricingConfig
                {
                    InputPerMillionTokens = source.Pricing.InputPerMillionTokens,
                    OutputPerMillionTokens = source.Pricing.OutputPerMillionTokens,
                    CacheReadPerMillionTokens = source.Pricing.CacheReadPerMillionTokens,
                    CacheWritePerMillionTokens = source.Pricing.CacheWritePerMillionTokens
                }
        };
    }
}

public sealed class ProviderModelViewModel
{
    public ProviderModelViewModel(string id, int? contextWindow, int? maxOutputTokens)
    {
        Id = id;
        ContextWindow = contextWindow;
        MaxOutputTokens = maxOutputTokens;
    }

    public string Id { get; }
    public int? ContextWindow { get; }
    public int? MaxOutputTokens { get; }
}
