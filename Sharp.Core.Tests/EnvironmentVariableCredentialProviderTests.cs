using Sharp.AI;

namespace Sharp.Core.Tests;

public sealed class EnvironmentVariableCredentialProviderTests
{
    [Fact]
    public async Task GetHeadersAsync_OpenAi_ReadsEnvironmentValueAndSupportsRotation()
    {
        var envName = $"SHARP_TEST_TOKEN_{Guid.NewGuid():N}";
        var provider = new EnvironmentVariableCredentialProvider(
            ProviderApiKind.OpenAiChatCompletions,
            [envName],
            fallbackToken: "fallback");

        try
        {
            Environment.SetEnvironmentVariable(envName, "token-a");
            var first = await provider.GetHeadersAsync(new LlmCredentialContext(
                new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
                "https://api.openai.com/v1/"));

            Assert.Equal("Bearer token-a", first["Authorization"]);

            Environment.SetEnvironmentVariable(envName, "token-b");
            var second = await provider.GetHeadersAsync(new LlmCredentialContext(
                new ModelDescriptor("openai", "gpt-4o-mini", ProviderApiKind.OpenAiChatCompletions),
                "https://api.openai.com/v1/"));

            Assert.Equal("Bearer token-b", second["Authorization"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    [Fact]
    public async Task GetHeadersAsync_Anthropic_UsesApiKeyAndVersionHeaders()
    {
        var provider = new EnvironmentVariableCredentialProvider(
            ProviderApiKind.AnthropicMessages,
            [],
            fallbackToken: "anthropic-token");

        var headers = await provider.GetHeadersAsync(new LlmCredentialContext(
            new ModelDescriptor("anthropic", "claude-sonnet-4-20250514", ProviderApiKind.AnthropicMessages),
            "https://api.anthropic.com/v1/"));

        Assert.Equal("anthropic-token", headers["x-api-key"]);
        Assert.Equal("2023-06-01", headers["anthropic-version"]);
    }

    [Fact]
    public async Task GetHeadersAsync_NoToken_ReturnsEmptyHeaders()
    {
        var provider = new EnvironmentVariableCredentialProvider(
            ProviderApiKind.OpenAiResponses,
            [],
            fallbackToken: null);

        var headers = await provider.GetHeadersAsync(new LlmCredentialContext(
            new ModelDescriptor("openai", "gpt-5-mini", ProviderApiKind.OpenAiResponses),
            "https://api.openai.com/v1/"));

        Assert.Empty(headers);
    }
}
