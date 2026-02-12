using Sharp.Core.Configuration;

namespace Sharp.Core.Tests;

public sealed class ProviderMergerTests
{
    private static List<ProviderConfig> CreateBuiltInProviders() =>
    [
        new ProviderConfig
        {
            Id = "openai",
            Api = ModelApiFormat.OpenAiCompletions,
            BaseUrl = "https://api.openai.com/v1/",
            Models =
            [
                new ModelConfig { Id = "gpt-4o-mini", ContextWindow = 128000, MaxOutputTokens = 16384 },
                new ModelConfig { Id = "gpt-4o", ContextWindow = 128000, MaxOutputTokens = 16384 }
            ]
        },
        new ProviderConfig
        {
            Id = "anthropic",
            Api = ModelApiFormat.AnthropicMessages,
            BaseUrl = "https://api.anthropic.com/v1/",
            Models =
            [
                new ModelConfig { Id = "claude-sonnet-4-20250514", ContextWindow = 200000, MaxOutputTokens = 8192 }
            ]
        }
    ];

    [Fact]
    public void Merge_NoCustomProviders_ReturnsDeepCopyOfBuiltIn()
    {
        var builtIn = CreateBuiltInProviders();

        var result = ProviderMerger.Merge(builtIn, []);

        Assert.Equal(2, result.Count);
        Assert.Equal("openai", result[0].Id);
        Assert.Equal("anthropic", result[1].Id);
        Assert.Equal(2, result[0].Models.Count);
    }

    [Fact]
    public void Merge_NewProvider_AppendsToList()
    {
        var custom = new ProviderConfig
        {
            Id = "ollama",
            Api = ModelApiFormat.OpenAiCompletions,
            BaseUrl = "http://localhost:11434/v1",
            Models =
            [
                new ModelConfig { Id = "llama3", Name = "Llama 3", ContextWindow = 128000, MaxOutputTokens = 32000 }
            ]
        };

        var result = ProviderMerger.Merge(CreateBuiltInProviders(), [custom]);

        Assert.Equal(3, result.Count);
        var ollama = result.First(p => p.Id == "ollama");
        Assert.Single(ollama.Models);
        Assert.Equal("Llama 3", ollama.Models[0].Name);
    }

    [Fact]
    public void Merge_NewProvider_AppliesDefaults()
    {
        var custom = new ProviderConfig
        {
            Id = "ollama",
            Api = ModelApiFormat.OpenAiCompletions,
            BaseUrl = "http://localhost:11434/v1",
            Models =
            [
                new ModelConfig { Id = "bare-model" }
            ]
        };

        var result = ProviderMerger.Merge(CreateBuiltInProviders(), [custom]);

        var model = result.First(p => p.Id == "ollama").Models[0];
        Assert.Equal("bare-model", model.Name);
        Assert.Equal(128000, model.ContextWindow);
        Assert.Equal(16384, model.MaxOutputTokens);
    }

    [Fact]
    public void Merge_ExistingProviderOverride_BaseUrl()
    {
        var custom = new ProviderConfig
        {
            Id = "anthropic",
            BaseUrl = "https://my-proxy.example.com/v1/"
        };

        var result = ProviderMerger.Merge(CreateBuiltInProviders(), [custom]);

        var provider = result.First(p => p.Id == "anthropic");
        Assert.Equal("https://my-proxy.example.com/v1/", provider.BaseUrl);
        Assert.Single(provider.Models);
    }

    [Fact]
    public void Merge_ExistingProviderOverride_Headers()
    {
        var custom = new ProviderConfig
        {
            Id = "anthropic",
            Headers = new Dictionary<string, string> { ["x-api-version"] = "2024-01" }
        };

        var result = ProviderMerger.Merge(CreateBuiltInProviders(), [custom]);

        var provider = result.First(p => p.Id == "anthropic");
        Assert.NotNull(provider.Headers);
        Assert.Equal("2024-01", provider.Headers!["x-api-version"]);
    }

    [Fact]
    public void Merge_ExistingProviderOverride_ApiKey()
    {
        var custom = new ProviderConfig
        {
            Id = "anthropic",
            ApiKey = "custom-key-from-config"
        };

        var result = ProviderMerger.Merge(CreateBuiltInProviders(), [custom]);

        var provider = result.First(p => p.Id == "anthropic");
        Assert.Equal("custom-key-from-config", provider.ApiKey);
    }

    [Fact]
    public void Merge_ModelUpsert_ReplacesExistingModel()
    {
        var custom = new ProviderConfig
        {
            Id = "openai",
            Models =
            [
                new ModelConfig { Id = "gpt-4o-mini", Name = "Custom Mini", ContextWindow = 256000, MaxOutputTokens = 32000 }
            ]
        };

        var result = ProviderMerger.Merge(CreateBuiltInProviders(), [custom]);

        var provider = result.First(p => p.Id == "openai");
        Assert.Equal(2, provider.Models.Count);
        var mini = provider.Models.First(m => m.Id == "gpt-4o-mini");
        Assert.Equal("Custom Mini", mini.Name);
        Assert.Equal(256000, mini.ContextWindow);
    }

    [Fact]
    public void Merge_ModelUpsert_AddsNewModel()
    {
        var custom = new ProviderConfig
        {
            Id = "openai",
            Models =
            [
                new ModelConfig { Id = "gpt-5", Name = "GPT-5", ContextWindow = 500000, MaxOutputTokens = 64000 }
            ]
        };

        var result = ProviderMerger.Merge(CreateBuiltInProviders(), [custom]);

        var provider = result.First(p => p.Id == "openai");
        Assert.Equal(3, provider.Models.Count);
        Assert.NotNull(provider.Models.FirstOrDefault(m => m.Id == "gpt-5"));
    }

    [Fact]
    public void Merge_ModelOverrides_PatchesName()
    {
        var custom = new ProviderConfig
        {
            Id = "openai",
            ModelOverrides = new Dictionary<string, ModelOverrideConfig>
            {
                ["gpt-4o-mini"] = new() { Name = "Patched Mini" }
            }
        };

        var result = ProviderMerger.Merge(CreateBuiltInProviders(), [custom]);

        var model = result.First(p => p.Id == "openai").Models.First(m => m.Id == "gpt-4o-mini");
        Assert.Equal("Patched Mini", model.Name);
    }

    [Fact]
    public void Merge_ModelOverrides_PatchesContextWindow()
    {
        var custom = new ProviderConfig
        {
            Id = "openai",
            ModelOverrides = new Dictionary<string, ModelOverrideConfig>
            {
                ["gpt-4o-mini"] = new() { ContextWindow = 256000 }
            }
        };

        var result = ProviderMerger.Merge(CreateBuiltInProviders(), [custom]);

        var model = result.First(p => p.Id == "openai").Models.First(m => m.Id == "gpt-4o-mini");
        Assert.Equal(256000, model.ContextWindow);

        var gpt4o = result.First(p => p.Id == "openai").Models.First(m => m.Id == "gpt-4o");
        Assert.Equal(128000, gpt4o.ContextWindow);
    }

    [Fact]
    public void Merge_ModelOverrides_PatchesReasoning()
    {
        var custom = new ProviderConfig
        {
            Id = "openai",
            ModelOverrides = new Dictionary<string, ModelOverrideConfig>
            {
                ["gpt-4o-mini"] = new() { Reasoning = true }
            }
        };

        var result = ProviderMerger.Merge(CreateBuiltInProviders(), [custom]);

        var model = result.First(p => p.Id == "openai").Models.First(m => m.Id == "gpt-4o-mini");
        Assert.NotNull(model.Capabilities);
        Assert.True(model.Capabilities!.SupportsReasoning);
    }

    [Fact]
    public void Merge_ModelOverrides_PatchesPricing()
    {
        var builtIn = CreateBuiltInProviders();
        builtIn[0].Models[0].Pricing = new ModelPricingConfig
        {
            InputPerMillionTokens = 1m,
            OutputPerMillionTokens = 2m,
            CacheReadPerMillionTokens = 0.5m
        };

        var custom = new ProviderConfig
        {
            Id = "openai",
            ModelOverrides = new Dictionary<string, ModelOverrideConfig>
            {
                ["gpt-4o-mini"] = new()
                {
                    Cost = new ModelPricingConfig { InputPerMillionTokens = 0.5m }
                }
            }
        };

        var result = ProviderMerger.Merge(builtIn, [custom]);

        var pricing = result.First(p => p.Id == "openai").Models.First(m => m.Id == "gpt-4o-mini").Pricing;
        Assert.NotNull(pricing);
        Assert.Equal(0.5m, pricing!.InputPerMillionTokens);
        Assert.Equal(2m, pricing.OutputPerMillionTokens);
        Assert.Equal(0.5m, pricing.CacheReadPerMillionTokens);
    }

    [Fact]
    public void Merge_ModelOverrides_PatchesHeaders()
    {
        var builtIn = CreateBuiltInProviders();
        builtIn[0].Models[0].Headers = new Dictionary<string, string> { ["x-existing"] = "keep" };

        var custom = new ProviderConfig
        {
            Id = "openai",
            ModelOverrides = new Dictionary<string, ModelOverrideConfig>
            {
                ["gpt-4o-mini"] = new()
                {
                    Headers = new Dictionary<string, string> { ["x-custom"] = "value" }
                }
            }
        };

        var result = ProviderMerger.Merge(builtIn, [custom]);

        var headers = result.First(p => p.Id == "openai").Models.First(m => m.Id == "gpt-4o-mini").Headers;
        Assert.NotNull(headers);
        Assert.Equal("keep", headers!["x-existing"]);
        Assert.Equal("value", headers["x-custom"]);
    }

    [Fact]
    public void Merge_ModelOverrides_PatchesCompat()
    {
        var builtIn = CreateBuiltInProviders();
        builtIn[0].Models[0].Compat = new OpenAiCompletionsCompatConfig
        {
            SupportsStore = true,
            SupportsDeveloperRole = true
        };

        var custom = new ProviderConfig
        {
            Id = "openai",
            ModelOverrides = new Dictionary<string, ModelOverrideConfig>
            {
                ["gpt-4o-mini"] = new()
                {
                    Compat = new OpenAiCompletionsCompatConfig { SupportsStore = false }
                }
            }
        };

        var result = ProviderMerger.Merge(builtIn, [custom]);

        var compat = result.First(p => p.Id == "openai").Models.First(m => m.Id == "gpt-4o-mini").Compat;
        Assert.NotNull(compat);
        Assert.False(compat!.SupportsStore);
        Assert.True(compat.SupportsDeveloperRole);
    }

    [Fact]
    public void Merge_DeepCopy_BuiltInNotMutated()
    {
        var builtIn = CreateBuiltInProviders();
        var originalBaseUrl = builtIn[1].BaseUrl;

        var custom = new ProviderConfig
        {
            Id = "anthropic",
            BaseUrl = "https://proxy.example.com/v1/"
        };

        ProviderMerger.Merge(builtIn, [custom]);

        Assert.Equal(originalBaseUrl, builtIn[1].BaseUrl);
    }

    [Fact]
    public void Merge_AuthHeader_AddsAuthorizationHeader()
    {
        var custom = new ProviderConfig
        {
            Id = "custom",
            Api = ModelApiFormat.OpenAiCompletions,
            BaseUrl = "http://localhost:8080/v1",
            ApiKey = "my-key",
            AuthHeader = true,
            Models =
            [
                new ModelConfig { Id = "model-a" }
            ]
        };

        var result = ProviderMerger.Merge(CreateBuiltInProviders(), [custom]);

        var model = result.First(p => p.Id == "custom").Models[0];
        Assert.NotNull(model.Headers);
        Assert.Equal("Bearer my-key", model.Headers!["Authorization"]);
    }

    [Fact]
    public void Merge_CaseInsensitive_ProviderMatch()
    {
        var custom = new ProviderConfig
        {
            Id = "OpenAI",
            BaseUrl = "https://proxy.example.com/v1/"
        };

        var result = ProviderMerger.Merge(CreateBuiltInProviders(), [custom]);

        Assert.Equal(2, result.Count);
        var provider = result.First(p => p.Id.Equals("openai", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("https://proxy.example.com/v1/", provider.BaseUrl);
    }
}
