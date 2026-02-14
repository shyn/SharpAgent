using System.Text.Json;
using Sharp.AI;
using Sharp.Core.Configuration;

namespace Sharp.Core.Tests;

public sealed class AgentConfigurationServiceTests
{
    private static readonly object EnvironmentLock = new();

    [Fact]
    public void BuildRuntimeOptions_ResolvesModelProviderAndKey()
    {
        var config = new AgentConfig
        {
            DefaultModel = "openai/gpt-4o-mini",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "openai",
                    Api = ModelApiFormat.OpenAiCompletions,
                    ApiKey = "test-key",
                    BaseUrl = "https://api.openai.com/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "gpt-4o-mini",
                            MaxOutputTokens = 2048
                        }
                    ]
                }
            ]
        };

        var service = new AgentConfigurationService(config);
        var options = service.BuildRuntimeOptions(thinkingLevel: ThinkingLevel.Low, maxTurns: 7);

        Assert.Equal("openai", options.Model.ProviderId);
        Assert.Equal("gpt-4o-mini", options.Model.ModelId);
        Assert.Equal(ProviderApiKind.OpenAiChatCompletions, options.Model.ApiKind);
        Assert.Equal("test-key", options.ApiKey);
        Assert.IsType<CachingBearerCredentialProvider>(options.CredentialProvider);
        Assert.Equal(ThinkingLevel.Low, options.ThinkingLevel);
        Assert.Equal(7, options.MaxTurns);
    }

    [Fact]
    public void ParseModelString_InvalidFormat_Throws()
    {
        Assert.Throws<ArgumentException>(() => AgentConfigurationService.ParseModelString("bad-format"));
    }

    [Fact]
    public void BuildRuntimeOptions_MapsModelCapabilitiesAndPricing()
    {
        var config = new AgentConfig
        {
            DefaultModel = "openai/gpt-4o-mini",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "openai",
                    Api = ModelApiFormat.OpenAiResponses,
                    ApiKey = "test-key",
                    BaseUrl = "https://api.openai.com/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "gpt-4o-mini",
                            Capabilities = new ModelCapabilitiesConfig
                            {
                                SupportsImageInput = false
                            },
                            Pricing = new ModelPricingConfig
                            {
                                InputPerMillionTokens = 2m,
                                OutputPerMillionTokens = 8m,
                                CacheReadPerMillionTokens = 0.4m
                            }
                        }
                    ]
                }
            ]
        };

        var service = new AgentConfigurationService(config);
        var options = service.BuildRuntimeOptions();

        Assert.NotNull(options.Model.Capabilities);
        Assert.True(options.Model.Capabilities!.SupportsReasoning);
        Assert.False(options.Model.Capabilities.SupportsImageInput);
        Assert.True(options.Model.Capabilities.SupportsToolCall);

        Assert.NotNull(options.Model.Pricing);
        Assert.Equal(2m, options.Model.Pricing!.InputPerMillionTokens);
        Assert.Equal(8m, options.Model.Pricing.OutputPerMillionTokens);
        Assert.Equal(0.4m, options.Model.Pricing.CacheReadPerMillionTokens);
        Assert.Equal(0m, options.Model.Pricing.CacheWritePerMillionTokens);
    }

    [Fact]
    public void AgentConfig_DefaultProviders_IncludePiAlignedBuiltInProviders()
    {
        var config = new AgentConfig();

        var expectedProviderIds = new[]
        {
            "openai",
            "anthropic",
            "openrouter",
            "xai",
            "groq",
            "cerebras",
            "zai",
            "mistral",
            "minimax",
            "minimax-cn",
            "huggingface",
            "opencode",
            "github-copilot",
            "kimi-coding"
        };

        foreach (var providerId in expectedProviderIds)
        {
            Assert.Single(
                config.Providers,
                p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        }

        var provider = Assert.Single(
            config.Providers,
            p => p.Id.Equals("kimi-coding", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ModelApiFormat.AnthropicMessages, provider.Api);
        Assert.Equal("https://api.kimi.com/coding/v1/", provider.BaseUrl);
        var kimiDefaultModel = Assert.Single(provider.Models);
        Assert.Equal("kimi-k2-thinking", kimiDefaultModel.Id);
    }

    [Fact]
    public void BuildRuntimeOptions_UsesProviderSpecificEnvironmentApiKey()
    {
        var config = new AgentConfig
        {
            DefaultModel = "kimi-coding/claude-sonnet-4-20250514",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "kimi-coding",
                    Api = ModelApiFormat.AnthropicMessages,
                    ApiKey = null,
                    BaseUrl = "https://api.kimi.com/coding/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "claude-sonnet-4-20250514"
                        }
                    ]
                }
            ]
        };

        WithEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["SHARP_KIMI_CODING_API_KEY"] = "env-kimi-key"
            },
            () =>
            {
                var service = new AgentConfigurationService(config);
                var options = service.BuildRuntimeOptions();
                Assert.Equal("env-kimi-key", options.ApiKey);
            });
    }

    [Fact]
    public void BuildRuntimeOptions_CanonicalKimiCodingProvider_UsesGlobalKimiAlias()
    {
        var config = new AgentConfig
        {
            DefaultModel = "kimi-coding/k2p5",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "kimi-coding",
                    Api = ModelApiFormat.AnthropicMessages,
                    ApiKey = null,
                    BaseUrl = "https://api.kimi.com/coding/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "k2p5"
                        }
                    ]
                }
            ]
        };

        WithEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["KIMI_API_KEY"] = "global-kimi-key",
                ["KIMI_BASE_URL"] = "https://kimi-proxy.example.com/coding/v1/"
            },
            () =>
            {
                var service = new AgentConfigurationService(config);
                var options = service.BuildRuntimeOptions();
                Assert.Equal("global-kimi-key", options.ApiKey);
                Assert.Equal("https://kimi-proxy.example.com/coding/v1/", options.BaseUrl);
            });
    }

    [Fact]
    public void BuildRuntimeOptions_CanonicalHuggingFaceProvider_UsesHfTokenAlias()
    {
        var config = new AgentConfig
        {
            DefaultModel = "huggingface/moonshotai/Kimi-K2.5",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "huggingface",
                    Api = ModelApiFormat.OpenAiCompletions,
                    ApiKey = null,
                    BaseUrl = "https://router.huggingface.co/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "moonshotai/Kimi-K2.5"
                        }
                    ]
                }
            ]
        };

        WithEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["HF_TOKEN"] = "hf-token-value"
            },
            () =>
            {
                var service = new AgentConfigurationService(config);
                var options = service.BuildRuntimeOptions();
                Assert.Equal("hf-token-value", options.ApiKey);
            });
    }

    [Fact]
    public void BuildRuntimeOptions_CanonicalGithubCopilotProvider_UsesGhTokenAlias()
    {
        var config = new AgentConfig
        {
            DefaultModel = "github-copilot/gpt-4o",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "github-copilot",
                    Api = ModelApiFormat.OpenAiCompletions,
                    ApiKey = null,
                    BaseUrl = "https://api.individual.githubcopilot.com/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "gpt-4o"
                        }
                    ]
                }
            ]
        };

        WithEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["GH_TOKEN"] = "gh-token-value"
            },
            () =>
            {
                var service = new AgentConfigurationService(config);
                var options = service.BuildRuntimeOptions();
                Assert.Equal("gh-token-value", options.ApiKey);
            });
    }

    [Fact]
    public void BuildRuntimeOptions_CustomAnthropicProvider_IgnoresGlobalAnthropicBaseUrlAlias()
    {
        var config = new AgentConfig
        {
            DefaultModel = "custom/gemini-3-flash",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "custom",
                    Api = ModelApiFormat.AnthropicMessages,
                    ApiKey = "dummy",
                    BaseUrl = "http://localhost:8045/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "gemini-3-flash"
                        }
                    ]
                }
            ]
        };

        WithEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["ANTHROPIC_BASE_URL"] = "https://api.kimi.com/coding/v1/"
            },
            () =>
            {
                var service = new AgentConfigurationService(config);
                var options = service.BuildRuntimeOptions();
                Assert.Equal("http://localhost:8045/v1/", options.BaseUrl);
            });
    }

    [Fact]
    public void BuildRuntimeOptions_CustomAnthropicProvider_IgnoresGlobalAnthropicApiKeyAlias()
    {
        var config = new AgentConfig
        {
            DefaultModel = "custom/gemini-3-flash",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "custom",
                    Api = ModelApiFormat.AnthropicMessages,
                    ApiKey = "config-key",
                    BaseUrl = "http://localhost:8045/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "gemini-3-flash"
                        }
                    ]
                }
            ]
        };

        WithEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["ANTHROPIC_API_KEY"] = "global-anthropic-key"
            },
            () =>
            {
                var service = new AgentConfigurationService(config);
                var options = service.BuildRuntimeOptions();
                Assert.Equal("config-key", options.ApiKey);
            });
    }

    [Fact]
    public void BuildRuntimeOptions_CanonicalAnthropicProvider_UsesGlobalAnthropicAlias()
    {
        var config = new AgentConfig
        {
            DefaultModel = "anthropic/claude-sonnet-4-20250514",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "anthropic",
                    Api = ModelApiFormat.AnthropicMessages,
                    ApiKey = null,
                    BaseUrl = "https://api.anthropic.com/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "claude-sonnet-4-20250514"
                        }
                    ]
                }
            ]
        };

        WithEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["ANTHROPIC_API_KEY"] = "global-anthropic-key",
                ["ANTHROPIC_BASE_URL"] = "https://proxy.example.com/v1/"
            },
            () =>
            {
                var service = new AgentConfigurationService(config);
                var options = service.BuildRuntimeOptions();
                Assert.Equal("global-anthropic-key", options.ApiKey);
                Assert.Equal("https://proxy.example.com/v1/", options.BaseUrl);
            });
    }

    [Fact]
    public void BuildRuntimeOptions_EnvironmentApiKeyOverridesConfiguredApiKey()
    {
        var config = new AgentConfig
        {
            DefaultModel = "test-provider/model-a",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "test-provider",
                    Api = ModelApiFormat.OpenAiCompletions,
                    ApiKey = "config-key",
                    BaseUrl = "https://example.com/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "model-a"
                        }
                    ]
                }
            ]
        };

        WithEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["SHARP_TEST_PROVIDER_API_KEY"] = "env-key"
            },
            () =>
            {
                var service = new AgentConfigurationService(config);
                var options = service.BuildRuntimeOptions();
                Assert.Equal("env-key", options.ApiKey);
            });
    }

    [Fact]
    public void BuildRuntimeOptions_CanonicalOpenAiProvider_UsesAccessTokenAlias()
    {
        var config = new AgentConfig
        {
            DefaultModel = "openai/gpt-4o-mini",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "openai",
                    Api = ModelApiFormat.OpenAiCompletions,
                    ApiKey = null,
                    BaseUrl = "https://api.openai.com/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "gpt-4o-mini"
                        }
                    ]
                }
            ]
        };

        WithEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["OPENAI_ACCESS_TOKEN"] = "openai-access-token"
            },
            () =>
            {
                var service = new AgentConfigurationService(config);
                var options = service.BuildRuntimeOptions();
                Assert.Equal("openai-access-token", options.ApiKey);
                Assert.IsType<CachingBearerCredentialProvider>(options.CredentialProvider);
            });
    }

    [Fact]
    public void BuildRuntimeOptions_AccessTokenJsonEnvelope_ProducesHeadersAndRefreshesAfterExpiry()
    {
        var config = new AgentConfig
        {
            DefaultModel = "my-provider/model-a",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "my-provider",
                    Api = ModelApiFormat.OpenAiCompletions,
                    ApiKey = null,
                    BaseUrl = "https://example.com/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "model-a"
                        }
                    ]
                }
            ]
        };

        var envName = "SHARP_MY_PROVIDER_ACCESS_TOKEN";
        var expired = DateTimeOffset.UtcNow.AddMinutes(-10);
        WithEnvironmentVariables(
            new Dictionary<string, string?>
            {
                [envName] = $"{{\"access_token\":\"token-1\",\"expires_at\":\"{expired:O}\"}}"
            },
            () =>
            {
                var service = new AgentConfigurationService(config);
                var options = service.BuildRuntimeOptions();
                using var credentialProvider = Assert.IsType<CachingBearerCredentialProvider>(options.CredentialProvider);

                var context = new LlmCredentialContext(options.Model, options.BaseUrl);
                var firstHeaders = credentialProvider.GetHeadersAsync(context).AsTask().GetAwaiter().GetResult();
                Assert.Equal("Bearer token-1", firstHeaders["Authorization"]);

                Environment.SetEnvironmentVariable(
                    envName,
                    """{"access_token":"token-2","expires_in":3600}""");
                var secondHeaders = credentialProvider.GetHeadersAsync(context).AsTask().GetAwaiter().GetResult();
                Assert.Equal("Bearer token-2", secondHeaders["Authorization"]);
            });
    }

    [Fact]
    public void BuildRuntimeOptions_PropagatesResourceLoadingOptions()
    {
        var config = new AgentConfig
        {
            Providers =
            [
                new ProviderConfig
                {
                    Id = "openai",
                    Api = ModelApiFormat.OpenAiCompletions,
                    ApiKey = "test-key",
                    BaseUrl = "https://api.openai.com/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "gpt-4o-mini"
                        }
                    ]
                }
            ]
        };

        var service = new AgentConfigurationService(config);
        var options = service.BuildRuntimeOptions(
            systemPrompt: "custom-system",
            appendSystemPrompt: "append",
            discoverSystemPromptFile: true,
            includeProjectContextFiles: false,
            enableSkills: false,
            includeDefaultSkills: false,
            skillPaths: ["./skills/path"],
            discoverExtensions: false,
            extensionPaths: ["./extensions/path"]);

        Assert.Equal("custom-system", options.SystemPrompt);
        Assert.Equal("append", options.AppendSystemPrompt);
        Assert.False(options.DiscoverSystemPromptFile);
        Assert.False(options.IncludeProjectContextFiles);
        Assert.False(options.EnableSkills);
        Assert.False(options.IncludeDefaultSkills);
        Assert.Single(options.SkillPaths!);
        Assert.False(options.DiscoverExtensions);
        Assert.Single(options.ExtensionPaths!);
    }

    [Fact]
    public void LoadFromFile_ParsesPiStyleProviderApiFormat()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sharp-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path,
                """
                {
                  "defaultModel": "openai/gpt-4o-mini",
                  "providers": [
                    {
                      "id": "openai",
                      "api": "openai-completions",
                      "apiKey": "test-key",
                      "baseUrl": "https://api.openai.com/v1/",
                      "models": [
                        { "id": "gpt-4o-mini" }
                      ]
                    }
                  ]
                }
                """);

            var service = AgentConfigurationService.LoadFromFile(path);
            var options = service.BuildRuntimeOptions();
            Assert.Equal(ProviderApiKind.OpenAiChatCompletions, options.Model.ApiKind);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_ParsesOpenAiResponsesApiFormat()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sharp-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path,
                """
                {
                  "defaultModel": "openai/gpt-5-mini",
                  "providers": [
                    {
                      "id": "openai",
                      "api": "openai-responses",
                      "apiKey": "test-key",
                      "baseUrl": "https://api.openai.com/v1/",
                      "models": [
                        { "id": "gpt-5-mini" }
                      ]
                    }
                  ]
                }
                """);

            var service = AgentConfigurationService.LoadFromFile(path);
            var options = service.BuildRuntimeOptions();
            Assert.Equal(ProviderApiKind.OpenAiResponses, options.Model.ApiKind);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_ParsesOpenAiCompletionsCompat()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sharp-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path,
                """
                {
                  "defaultModel": "openai/gpt-4o-mini",
                  "providers": [
                    {
                      "id": "openai",
                      "api": "openai-completions",
                      "apiKey": "test-key",
                      "baseUrl": "https://api.openai.com/v1/",
                      "models": [
                        {
                          "id": "gpt-4o-mini",
                          "compat": {
                            "supportsStore": false,
                            "supportsDeveloperRole": false,
                            "supportsReasoningEffort": false,
                            "supportsUsageInStreaming": false,
                            "supportsStrictMode": false,
                            "requiresToolResultName": true,
                            "requiresAssistantAfterToolResult": true,
                            "requiresMistralToolIds": true,
                            "requiresThinkingAsText": true,
                            "maxTokensField": "max_completion_tokens",
                            "thinkingFormat": "zai",
                            "openRouterRouting": {
                              "only": ["anthropic", "openai"],
                              "order": ["openai", "anthropic"]
                            },
                            "vercelGatewayRouting": {
                              "only": ["gateway-a"],
                              "order": ["gateway-b", "gateway-a"]
                            }
                          }
                        }
                      ]
                    }
                  ]
                }
                """);

            var service = AgentConfigurationService.LoadFromFile(path);
            var options = service.BuildRuntimeOptions();

            Assert.Equal(ProviderApiKind.OpenAiChatCompletions, options.Model.ApiKind);
            Assert.NotNull(options.Model.OpenAiCompletionsCompat);
            Assert.False(options.Model.OpenAiCompletionsCompat!.SupportsUsageInStreaming);
            Assert.False(options.Model.OpenAiCompletionsCompat.SupportsStrictMode);
            Assert.True(options.Model.OpenAiCompletionsCompat.RequiresToolResultName);
            Assert.True(options.Model.OpenAiCompletionsCompat.RequiresAssistantAfterToolResult);
            Assert.True(options.Model.OpenAiCompletionsCompat.RequiresMistralToolIds);
            Assert.True(options.Model.OpenAiCompletionsCompat.RequiresThinkingAsText);
            Assert.Equal(OpenAiMaxTokensField.MaxCompletionTokens, options.Model.OpenAiCompletionsCompat.MaxTokensField);
            Assert.Equal(false, options.Model.OpenAiCompletionsCompat.SupportsStore);
            Assert.Equal(false, options.Model.OpenAiCompletionsCompat.SupportsDeveloperRole);
            Assert.Equal(false, options.Model.OpenAiCompletionsCompat.SupportsReasoningEffort);
            Assert.Equal(OpenAiThinkingFormats.Zai, options.Model.OpenAiCompletionsCompat.ThinkingFormat);
            var openRouterRouting = options.Model.OpenAiCompletionsCompat.OpenRouterRouting;
            Assert.NotNull(openRouterRouting);
            Assert.NotNull(openRouterRouting!.Only);
            Assert.Equal(["anthropic", "openai"], openRouterRouting.Only);
            Assert.NotNull(openRouterRouting.Order);
            Assert.Equal(["openai", "anthropic"], openRouterRouting.Order);
            var vercelGatewayRouting = options.Model.OpenAiCompletionsCompat.VercelGatewayRouting;
            Assert.NotNull(vercelGatewayRouting);
            Assert.NotNull(vercelGatewayRouting!.Only);
            Assert.Equal(["gateway-a"], vercelGatewayRouting.Only);
            Assert.NotNull(vercelGatewayRouting.Order);
            Assert.Equal(["gateway-b", "gateway-a"], vercelGatewayRouting.Order);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void BuildRuntimeOptions_OpenAiCompletions_InfersCompatFromMistralBaseUrl()
    {
        var config = new AgentConfig
        {
            DefaultModel = "mistral/open-mistral-nemo",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "mistral",
                    Api = ModelApiFormat.OpenAiCompletions,
                    ApiKey = "test-key",
                    BaseUrl = "https://api.mistral.ai/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "open-mistral-nemo"
                        }
                    ]
                }
            ]
        };

        var service = new AgentConfigurationService(config);
        var options = service.BuildRuntimeOptions();
        var compat = options.Model.OpenAiCompletionsCompat;

        Assert.NotNull(compat);
        Assert.True(compat!.RequiresToolResultName);
        Assert.True(compat.RequiresMistralToolIds);
        Assert.True(compat.RequiresThinkingAsText);
        Assert.Equal(OpenAiMaxTokensField.MaxTokens, compat.MaxTokensField);
        Assert.Equal(false, compat.SupportsStore);
        Assert.Equal(false, compat.SupportsDeveloperRole);
        Assert.Equal(true, compat.SupportsReasoningEffort);
        Assert.Equal(OpenAiThinkingFormats.OpenAi, compat.ThinkingFormat);
        Assert.Null(compat.OpenRouterRouting);
        Assert.Null(compat.VercelGatewayRouting);
    }

    [Fact]
    public void BuildRuntimeOptions_OpenAiCompletions_ExplicitCompatOverridesInferredDefaults()
    {
        var config = new AgentConfig
        {
            DefaultModel = "proxy/open-mistral-nemo",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "proxy",
                    Api = ModelApiFormat.OpenAiCompletions,
                    ApiKey = "test-key",
                    BaseUrl = "https://api.mistral.ai/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "open-mistral-nemo",
                            Compat = new OpenAiCompletionsCompatConfig
                            {
                                SupportsStore = true,
                                SupportsDeveloperRole = true,
                                SupportsReasoningEffort = false,
                                SupportsUsageInStreaming = false,
                                RequiresMistralToolIds = false,
                                MaxTokensField = "max_completion_tokens",
                                ThinkingFormat = OpenAiThinkingFormats.Qwen,
                                OpenRouterRouting = new OpenAiRoutingPreferences(
                                    Only: ["anthropic"],
                                    Order: ["openai", "anthropic"]),
                                VercelGatewayRouting = new OpenAiRoutingPreferences(
                                    Only: ["gateway-openai"])
                            }
                        }
                    ]
                }
            ]
        };

        var service = new AgentConfigurationService(config);
        var options = service.BuildRuntimeOptions();
        var compat = options.Model.OpenAiCompletionsCompat;

        Assert.NotNull(compat);
        Assert.False(compat!.SupportsUsageInStreaming);
        Assert.False(compat.RequiresMistralToolIds);
        Assert.Equal(OpenAiMaxTokensField.MaxCompletionTokens, compat.MaxTokensField);
        Assert.True(compat.RequiresToolResultName);
        Assert.True(compat.RequiresThinkingAsText);
        Assert.Equal(true, compat.SupportsStore);
        Assert.Equal(true, compat.SupportsDeveloperRole);
        Assert.Equal(false, compat.SupportsReasoningEffort);
        Assert.Equal(OpenAiThinkingFormats.Qwen, compat.ThinkingFormat);
        var openRouterRouting = compat.OpenRouterRouting;
        Assert.NotNull(openRouterRouting);
        Assert.NotNull(openRouterRouting!.Only);
        Assert.Equal(["anthropic"], openRouterRouting.Only);
        Assert.NotNull(openRouterRouting.Order);
        Assert.Equal(["openai", "anthropic"], openRouterRouting.Order);
        var vercelGatewayRouting = compat.VercelGatewayRouting;
        Assert.NotNull(vercelGatewayRouting);
        Assert.NotNull(vercelGatewayRouting!.Only);
        Assert.Equal(["gateway-openai"], vercelGatewayRouting.Only);
    }

    [Fact]
    public void BuildRuntimeOptions_OpenAiCompletions_InfersCompatMatrixForKnownProvidersAndUrls()
    {
        var chutesCompat = BuildOpenAiCompletionsCompat("proxy", "https://api.chutes.ai/v1/");
        Assert.True(chutesCompat.SupportsUsageInStreaming);
        Assert.Equal(OpenAiMaxTokensField.MaxTokens, chutesCompat.MaxTokensField);
        Assert.Equal(false, chutesCompat.SupportsStore);
        Assert.Equal(false, chutesCompat.SupportsDeveloperRole);
        Assert.Equal(true, chutesCompat.SupportsReasoningEffort);
        Assert.Equal(OpenAiThinkingFormats.OpenAi, chutesCompat.ThinkingFormat);

        var gatewayzCompat = BuildOpenAiCompletionsCompat("proxy", "https://api.gatewayz.ai/v1/");
        Assert.False(gatewayzCompat.SupportsUsageInStreaming);
        Assert.Equal(OpenAiMaxTokensField.MaxCompletionTokens, gatewayzCompat.MaxTokensField);
        Assert.Equal(true, gatewayzCompat.SupportsStore);
        Assert.Equal(true, gatewayzCompat.SupportsDeveloperRole);
        Assert.Equal(true, gatewayzCompat.SupportsReasoningEffort);
        Assert.Equal(OpenAiThinkingFormats.OpenAi, gatewayzCompat.ThinkingFormat);

        var deepseekCompat = BuildOpenAiCompletionsCompat("proxy", "https://api.deepseek.com/v1/");
        Assert.True(deepseekCompat.SupportsUsageInStreaming);
        Assert.Equal(OpenAiMaxTokensField.MaxCompletionTokens, deepseekCompat.MaxTokensField);
        Assert.Equal(false, deepseekCompat.SupportsStore);
        Assert.Equal(false, deepseekCompat.SupportsDeveloperRole);
        Assert.Equal(true, deepseekCompat.SupportsReasoningEffort);
        Assert.Equal(OpenAiThinkingFormats.OpenAi, deepseekCompat.ThinkingFormat);

        var zaiCompat = BuildOpenAiCompletionsCompat("zai", "https://proxy.example.com/v1/");
        Assert.True(zaiCompat.SupportsUsageInStreaming);
        Assert.Equal(OpenAiMaxTokensField.MaxCompletionTokens, zaiCompat.MaxTokensField);
        Assert.Equal(false, zaiCompat.SupportsStore);
        Assert.Equal(false, zaiCompat.SupportsDeveloperRole);
        Assert.Equal(false, zaiCompat.SupportsReasoningEffort);
        Assert.Equal(OpenAiThinkingFormats.Zai, zaiCompat.ThinkingFormat);

        var qwenCompat = BuildOpenAiCompletionsCompat("proxy", "https://dashscope.aliyuncs.com/compatible-mode/v1");
        Assert.True(qwenCompat.SupportsUsageInStreaming);
        Assert.Equal(OpenAiMaxTokensField.MaxCompletionTokens, qwenCompat.MaxTokensField);
        Assert.Equal(true, qwenCompat.SupportsStore);
        Assert.Equal(true, qwenCompat.SupportsDeveloperRole);
        Assert.Equal(true, qwenCompat.SupportsReasoningEffort);
        Assert.Equal(OpenAiThinkingFormats.Qwen, qwenCompat.ThinkingFormat);

        var xaiByBaseUrlCompat = BuildOpenAiCompletionsCompat("proxy", "https://api.x.ai/v1/");
        Assert.Equal(false, xaiByBaseUrlCompat.SupportsStore);
        Assert.Equal(false, xaiByBaseUrlCompat.SupportsDeveloperRole);
        Assert.Equal(false, xaiByBaseUrlCompat.SupportsReasoningEffort);
    }

    [Fact]
    public void BuildRuntimeOptions_OpenAiCompletions_ExplicitCompatTakesPrecedenceOverGatewayzInference()
    {
        var compat = BuildOpenAiCompletionsCompat(
            providerId: "proxy",
            baseUrl: "https://api.gatewayz.ai/v1/",
            compat: new OpenAiCompletionsCompatConfig
            {
                SupportsUsageInStreaming = true,
                SupportsStore = false,
                SupportsDeveloperRole = false,
                SupportsReasoningEffort = false,
                ThinkingFormat = OpenAiThinkingFormats.Zai
            });

        Assert.True(compat.SupportsUsageInStreaming);
        Assert.Equal(false, compat.SupportsStore);
        Assert.Equal(false, compat.SupportsDeveloperRole);
        Assert.Equal(false, compat.SupportsReasoningEffort);
        Assert.Equal(OpenAiThinkingFormats.Zai, compat.ThinkingFormat);
        Assert.Equal(OpenAiMaxTokensField.MaxCompletionTokens, compat.MaxTokensField);
    }

    [Fact]
    public void BuildRuntimeOptions_OpenAiCompletions_InvalidThinkingFormat_ThrowsJsonException()
    {
        var config = new AgentConfig
        {
            DefaultModel = "openai/gpt-4o-mini",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "openai",
                    Api = ModelApiFormat.OpenAiCompletions,
                    ApiKey = "test-key",
                    BaseUrl = "https://api.openai.com/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "gpt-4o-mini",
                            Compat = new OpenAiCompletionsCompatConfig
                            {
                                ThinkingFormat = "unsupported-thinking-format"
                            }
                        }
                    ]
                }
            ]
        };

        var service = new AgentConfigurationService(config);
        Assert.Throws<JsonException>(() => service.BuildRuntimeOptions());
    }

    [Fact]
    public void LoadFromFile_ParsesLegacyModelApiFormatForBackwardCompatibility()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sharp-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path,
                """
                {
                  "defaultModel": "anthropic/claude-sonnet-4-20250514",
                  "providers": [
                    {
                      "id": "anthropic",
                      "apiKey": "test-key",
                      "baseUrl": "https://api.anthropic.com/v1/",
                      "models": [
                        {
                          "id": "claude-sonnet-4-20250514",
                          "api": "AnthropicMessages"
                        }
                      ]
                    }
                  ]
                }
                """);

            var service = AgentConfigurationService.LoadFromFile(path);
            var options = service.BuildRuntimeOptions();
            Assert.Equal(ProviderApiKind.AnthropicMessages, options.Model.ApiKind);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ValidateConfig_MissingDefaultProviderApiKey_ReturnsError()
    {
        var config = new AgentConfig
        {
            DefaultModel = "openai/gpt-4o-mini",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "openai",
                    Api = ModelApiFormat.OpenAiCompletions,
                    BaseUrl = "https://api.openai.com/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "gpt-4o-mini"
                        }
                    ]
                }
            ]
        };

        var service = new AgentConfigurationService(config);
        var validation = service.ValidateConfig();

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error =>
            error.Contains("Missing API key for defaultModel provider 'openai'", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateConfig_WithLegacyModelApi_ReturnsWarning()
    {
        var config = new AgentConfig
        {
            DefaultModel = "openai/gpt-4o-mini",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "openai",
                    Api = ModelApiFormat.OpenAiCompletions,
                    ApiKey = "test-key",
                    BaseUrl = "https://api.openai.com/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "gpt-4o-mini",
                            Api = ModelApiFormat.OpenAiCompletions
                        }
                    ]
                }
            ]
        };

        var service = new AgentConfigurationService(config);
        var validation = service.ValidateConfig();

        Assert.True(validation.IsValid);
        Assert.Contains(validation.Warnings, warning =>
            warning.Contains("legacy field 'api'", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateConfig_MissingDefaultProviderApiKey_WithEnvironmentOverride_IsValid()
    {
        var config = new AgentConfig
        {
            DefaultModel = "my-provider/model-a",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "my-provider",
                    Api = ModelApiFormat.OpenAiCompletions,
                    ApiKey = null,
                    BaseUrl = "https://example.com/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "model-a"
                        }
                    ]
                }
            ]
        };

        WithEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["SHARP_MY_PROVIDER_API_KEY"] = "env-key"
            },
            () =>
            {
                var service = new AgentConfigurationService(config);
                var validation = service.ValidateConfig();
                Assert.True(validation.IsValid);
            });
    }

    [Fact]
    public void ValidateConfig_MissingDefaultProviderApiKey_WithAccessTokenOverride_IsValid()
    {
        var config = new AgentConfig
        {
            DefaultModel = "my-provider/model-a",
            Providers =
            [
                new ProviderConfig
                {
                    Id = "my-provider",
                    Api = ModelApiFormat.OpenAiCompletions,
                    ApiKey = null,
                    BaseUrl = "https://example.com/v1/",
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "model-a"
                        }
                    ]
                }
            ]
        };

        WithEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["SHARP_MY_PROVIDER_ACCESS_TOKEN"] = "env-access-token"
            },
            () =>
            {
                var service = new AgentConfigurationService(config);
                var validation = service.ValidateConfig();
                Assert.True(validation.IsValid);
            });
    }

    private static OpenAiCompletionsCompat BuildOpenAiCompletionsCompat(
        string providerId,
        string baseUrl,
        OpenAiCompletionsCompatConfig? compat = null)
    {
        var config = new AgentConfig
        {
            DefaultModel = $"{providerId}/model",
            Providers =
            [
                new ProviderConfig
                {
                    Id = providerId,
                    Api = ModelApiFormat.OpenAiCompletions,
                    ApiKey = "test-key",
                    BaseUrl = baseUrl,
                    Models =
                    [
                        new ModelConfig
                        {
                            Id = "model",
                            Compat = compat
                        }
                    ]
                }
            ]
        };

        var service = new AgentConfigurationService(config);
        var resolvedCompat = service.BuildRuntimeOptions().Model.OpenAiCompletionsCompat;
        Assert.NotNull(resolvedCompat);
        return resolvedCompat!;
    }

    private static void WithEnvironmentVariables(
        IReadOnlyDictionary<string, string?> overrides,
        Action action)
    {
        lock (EnvironmentLock)
        {
            var previous = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var pair in overrides)
            {
                previous[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }

            try
            {
                action();
            }
            finally
            {
                foreach (var pair in previous)
                    Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sharpagent-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
