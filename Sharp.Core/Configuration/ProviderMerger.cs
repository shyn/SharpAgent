namespace Sharp.Core.Configuration;

public static class ProviderMerger
{
    private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;

    public static List<ProviderConfig> Merge(
        IReadOnlyList<ProviderConfig> builtInProviders,
        IReadOnlyList<ProviderConfig> customProviders)
    {
        var providers = DeepCopyProviders(builtInProviders);

        foreach (var custom in customProviders)
        {
            var existing = providers.FirstOrDefault(p =>
                p.Id.Equals(custom.Id, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                ApplyProviderOverrides(existing, custom);
                ApplyModelOverrides(existing, custom.ModelOverrides);
                UpsertModels(existing, custom.Models);
                ApplyAuthHeader(existing, custom);
            }
            else
            {
                providers.Add(BuildNewProvider(custom));
            }
        }

        return providers;
    }

    private static void ApplyProviderOverrides(ProviderConfig existing, ProviderConfig custom)
    {
        if (custom.Models.Count > 0)
            existing.Api = custom.Api;

        if (!string.IsNullOrEmpty(custom.BaseUrl))
            existing.BaseUrl = custom.BaseUrl;

        if (custom.Headers is not null)
        {
            var resolvedHeaders = ConfigValueResolver.ResolveHeaders(custom.Headers);
            if (resolvedHeaders is not null)
            {
                existing.Headers ??= new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (key, value) in resolvedHeaders)
                    existing.Headers[key] = value;
            }
        }

        if (custom.ApiKey is not null)
        {
            var resolvedKey = ConfigValueResolver.Resolve(custom.ApiKey);
            if (resolvedKey is not null)
                existing.ApiKey = resolvedKey;
        }
    }

    private static void ApplyModelOverrides(
        ProviderConfig provider,
        Dictionary<string, ModelOverrideConfig>? modelOverrides)
    {
        if (modelOverrides is not { Count: > 0 }) return;

        foreach (var model in provider.Models)
        {
            if (modelOverrides.TryGetValue(model.Id, out var over))
                ApplyModelOverride(model, over);
        }
    }

    private static void UpsertModels(ProviderConfig existing, List<ModelConfig> customModels)
    {
        if (customModels.Count == 0) return;

        foreach (var customModel in customModels)
        {
            var idx = existing.Models.FindIndex(m =>
                m.Id.Equals(customModel.Id, StringComparison.OrdinalIgnoreCase));

            if (idx >= 0)
                existing.Models[idx] = customModel;
            else
                existing.Models.Add(customModel);
        }
    }

    private static void ApplyAuthHeader(ProviderConfig existing, ProviderConfig custom)
    {
        if (custom.AuthHeader != true || custom.ApiKey is null) return;

        var resolvedKey = ConfigValueResolver.Resolve(custom.ApiKey);
        if (resolvedKey is null) return;

        foreach (var model in existing.Models)
        {
            model.Headers ??= new Dictionary<string, string>(StringComparer.Ordinal);
            model.Headers.TryAdd("Authorization", $"Bearer {resolvedKey}");
        }
    }

    private static ProviderConfig BuildNewProvider(ProviderConfig custom)
    {
        var resolvedKey = ConfigValueResolver.Resolve(custom.ApiKey);
        var providerHeaders = ConfigValueResolver.ResolveHeaders(custom.Headers);

        var models = new List<ModelConfig>(custom.Models.Count);
        foreach (var m in custom.Models)
        {
            var modelHeaders = ConfigValueResolver.ResolveHeaders(m.Headers);
            var merged = MergeHeaderDicts(providerHeaders, modelHeaders);

            if (custom.AuthHeader == true && resolvedKey is not null)
            {
                merged ??= new Dictionary<string, string>(StringComparer.Ordinal);
                merged.TryAdd("Authorization", $"Bearer {resolvedKey}");
            }

            models.Add(new ModelConfig
            {
                Id = m.Id,
                Name = m.Name ?? m.Id,
                Api = m.Api,
                Capabilities = m.Capabilities,
                Pricing = m.Pricing,
                ContextWindow = m.ContextWindow ?? 128000,
                MaxOutputTokens = m.MaxOutputTokens ?? 16384,
                Compat = m.Compat,
                Headers = merged
            });
        }

        return new ProviderConfig
        {
            Id = custom.Id,
            Api = custom.Api,
            BaseUrl = custom.BaseUrl,
            ApiKey = custom.ApiKey,
            AuthHeader = custom.AuthHeader,
            Headers = custom.Headers,
            Models = models
        };
    }

    private static void ApplyModelOverride(ModelConfig model, ModelOverrideConfig over)
    {
        if (over.Name is not null)
            model.Name = over.Name;

        if (over.Reasoning is not null)
        {
            model.Capabilities ??= new ModelCapabilitiesConfig();
            model.Capabilities.SupportsReasoning = over.Reasoning;
        }

        if (over.Input is not null)
        {
            model.Capabilities ??= new ModelCapabilitiesConfig();
            model.Capabilities.SupportsImageInput = over.Input.Contains("image", StringComparer.OrdinalIgnoreCase);
        }

        if (over.ContextWindow is not null)
            model.ContextWindow = over.ContextWindow;

        if (over.MaxOutputTokens is not null)
            model.MaxOutputTokens = over.MaxOutputTokens;

        if (over.Cost is not null)
        {
            model.Pricing ??= new ModelPricingConfig();
            if (over.Cost.InputPerMillionTokens is not null)
                model.Pricing.InputPerMillionTokens = over.Cost.InputPerMillionTokens;
            if (over.Cost.OutputPerMillionTokens is not null)
                model.Pricing.OutputPerMillionTokens = over.Cost.OutputPerMillionTokens;
            if (over.Cost.CacheReadPerMillionTokens is not null)
                model.Pricing.CacheReadPerMillionTokens = over.Cost.CacheReadPerMillionTokens;
            if (over.Cost.CacheWritePerMillionTokens is not null)
                model.Pricing.CacheWritePerMillionTokens = over.Cost.CacheWritePerMillionTokens;
        }

        if (over.Headers is not null)
        {
            var resolvedHeaders = ConfigValueResolver.ResolveHeaders(over.Headers);
            if (resolvedHeaders is not null)
            {
                model.Headers ??= new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (key, value) in resolvedHeaders)
                    model.Headers[key] = value;
            }
        }

        if (over.Compat is not null)
        {
            model.Compat = MergeCompat(model.Compat, over.Compat);
        }
    }

    private static OpenAiCompletionsCompatConfig MergeCompat(
        OpenAiCompletionsCompatConfig? baseCompat,
        OpenAiCompletionsCompatConfig overrideCompat)
    {
        var merged = baseCompat is not null
            ? new OpenAiCompletionsCompatConfig
            {
                SupportsStore = baseCompat.SupportsStore,
                SupportsDeveloperRole = baseCompat.SupportsDeveloperRole,
                SupportsReasoningEffort = baseCompat.SupportsReasoningEffort,
                SupportsUsageInStreaming = baseCompat.SupportsUsageInStreaming,
                SupportsStrictMode = baseCompat.SupportsStrictMode,
                RequiresToolResultName = baseCompat.RequiresToolResultName,
                RequiresAssistantAfterToolResult = baseCompat.RequiresAssistantAfterToolResult,
                RequiresMistralToolIds = baseCompat.RequiresMistralToolIds,
                RequiresThinkingAsText = baseCompat.RequiresThinkingAsText,
                MaxTokensField = baseCompat.MaxTokensField,
                ThinkingFormat = baseCompat.ThinkingFormat,
                OpenRouterRouting = baseCompat.OpenRouterRouting,
                VercelGatewayRouting = baseCompat.VercelGatewayRouting
            }
            : new OpenAiCompletionsCompatConfig();

        if (overrideCompat.SupportsStore is not null) merged.SupportsStore = overrideCompat.SupportsStore;
        if (overrideCompat.SupportsDeveloperRole is not null) merged.SupportsDeveloperRole = overrideCompat.SupportsDeveloperRole;
        if (overrideCompat.SupportsReasoningEffort is not null) merged.SupportsReasoningEffort = overrideCompat.SupportsReasoningEffort;
        if (overrideCompat.SupportsUsageInStreaming is not null) merged.SupportsUsageInStreaming = overrideCompat.SupportsUsageInStreaming;
        if (overrideCompat.SupportsStrictMode is not null) merged.SupportsStrictMode = overrideCompat.SupportsStrictMode;
        if (overrideCompat.RequiresToolResultName is not null) merged.RequiresToolResultName = overrideCompat.RequiresToolResultName;
        if (overrideCompat.RequiresAssistantAfterToolResult is not null) merged.RequiresAssistantAfterToolResult = overrideCompat.RequiresAssistantAfterToolResult;
        if (overrideCompat.RequiresMistralToolIds is not null) merged.RequiresMistralToolIds = overrideCompat.RequiresMistralToolIds;
        if (overrideCompat.RequiresThinkingAsText is not null) merged.RequiresThinkingAsText = overrideCompat.RequiresThinkingAsText;
        if (overrideCompat.MaxTokensField is not null) merged.MaxTokensField = overrideCompat.MaxTokensField;
        if (overrideCompat.ThinkingFormat is not null) merged.ThinkingFormat = overrideCompat.ThinkingFormat;
        if (overrideCompat.OpenRouterRouting is not null) merged.OpenRouterRouting = overrideCompat.OpenRouterRouting;
        if (overrideCompat.VercelGatewayRouting is not null) merged.VercelGatewayRouting = overrideCompat.VercelGatewayRouting;

        return merged;
    }

    private static Dictionary<string, string>? MergeHeaderDicts(
        IReadOnlyDictionary<string, string>? baseHeaders,
        IReadOnlyDictionary<string, string>? overrideHeaders)
    {
        if (baseHeaders is null && overrideHeaders is null)
            return null;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (baseHeaders is not null)
            foreach (var (key, value) in baseHeaders)
                result[key] = value;

        if (overrideHeaders is not null)
            foreach (var (key, value) in overrideHeaders)
                result[key] = value;

        return result.Count > 0 ? result : null;
    }

    private static List<ProviderConfig> DeepCopyProviders(IReadOnlyList<ProviderConfig> source)
    {
        var result = new List<ProviderConfig>(source.Count);
        foreach (var p in source)
        {
            result.Add(new ProviderConfig
            {
                Id = p.Id,
                Api = p.Api,
                ApiKey = p.ApiKey,
                BaseUrl = p.BaseUrl,
                AuthHeader = p.AuthHeader,
                Headers = p.Headers is not null
                    ? new Dictionary<string, string>(p.Headers, StringComparer.Ordinal)
                    : null,
                Models = p.Models.Select(m => new ModelConfig
                {
                    Id = m.Id,
                    Name = m.Name,
                    Api = m.Api,
                    Compat = m.Compat,
                    Capabilities = m.Capabilities is not null
                        ? new ModelCapabilitiesConfig
                        {
                            SupportsReasoning = m.Capabilities.SupportsReasoning,
                            SupportsImageInput = m.Capabilities.SupportsImageInput,
                            SupportsToolCall = m.Capabilities.SupportsToolCall
                        }
                        : null,
                    Pricing = m.Pricing is not null
                        ? new ModelPricingConfig
                        {
                            InputPerMillionTokens = m.Pricing.InputPerMillionTokens,
                            OutputPerMillionTokens = m.Pricing.OutputPerMillionTokens,
                            CacheReadPerMillionTokens = m.Pricing.CacheReadPerMillionTokens,
                            CacheWritePerMillionTokens = m.Pricing.CacheWritePerMillionTokens
                        }
                        : null,
                    ContextWindow = m.ContextWindow,
                    MaxOutputTokens = m.MaxOutputTokens,
                    Headers = m.Headers is not null
                        ? new Dictionary<string, string>(m.Headers, StringComparer.Ordinal)
                        : null
                }).ToList()
            });
        }

        return result;
    }
}
