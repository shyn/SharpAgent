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
        Assert.Equal(ThinkingLevel.Low, options.ThinkingLevel);
        Assert.Equal(7, options.MaxTurns);
    }

    [Fact]
    public void ParseModelString_InvalidFormat_Throws()
    {
        Assert.Throws<ArgumentException>(() => AgentConfigurationService.ParseModelString("bad-format"));
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
}
