using Sharp.AI;

namespace Sharp.Core.Tests;

public sealed class EnvironmentVariableBearerCredentialTokenSourceTests
{
    [Fact]
    public async Task GetTokenAsync_PlainToken_ReturnsTokenWithoutExpiry()
    {
        var envName = $"SHARP_TEST_BEARER_{Guid.NewGuid():N}";
        var source = new EnvironmentVariableBearerTokenSource([envName], fallbackToken: "fallback-token");

        try
        {
            Environment.SetEnvironmentVariable(envName, "plain-token");
            var token = await source.GetTokenAsync(CreateContext());

            Assert.NotNull(token);
            Assert.Equal("plain-token", token!.Value);
            Assert.Null(token.ExpiresAt);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    [Fact]
    public async Task GetTokenAsync_JsonEnvelope_ReturnsTokenField()
    {
        var envName = $"SHARP_TEST_BEARER_{Guid.NewGuid():N}";
        var source = new EnvironmentVariableBearerTokenSource([envName], fallbackToken: null);

        try
        {
            Environment.SetEnvironmentVariable(envName, """{"access_token":"json-token"}""");
            var token = await source.GetTokenAsync(CreateContext());

            Assert.NotNull(token);
            Assert.Equal("json-token", token!.Value);
            Assert.Null(token.ExpiresAt);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    [Fact]
    public async Task GetTokenAsync_JsonEnvelope_ParsesExpiresInAndExpiresAt()
    {
        var envName = $"SHARP_TEST_BEARER_{Guid.NewGuid():N}";
        var source = new EnvironmentVariableBearerTokenSource([envName], fallbackToken: null);

        try
        {
            Environment.SetEnvironmentVariable(envName, """{"token":"expires-in-token","expires_in":120}""");
            var tokenWithExpiresIn = await source.GetTokenAsync(CreateContext());

            Assert.NotNull(tokenWithExpiresIn);
            Assert.Equal("expires-in-token", tokenWithExpiresIn!.Value);
            Assert.NotNull(tokenWithExpiresIn.ExpiresAt);
            var remainingSeconds = (tokenWithExpiresIn.ExpiresAt!.Value - DateTimeOffset.UtcNow).TotalSeconds;
            Assert.InRange(remainingSeconds, 100, 140);

            var expectedIsoExpiry = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
            Environment.SetEnvironmentVariable(
                envName,
                $"{{\"value\":\"expires-at-iso-token\",\"expires_at\":\"{expectedIsoExpiry:O}\"}}");
            var tokenWithIsoExpiry = await source.GetTokenAsync(CreateContext());

            Assert.NotNull(tokenWithIsoExpiry);
            Assert.Equal("expires-at-iso-token", tokenWithIsoExpiry!.Value);
            Assert.Equal(expectedIsoExpiry, tokenWithIsoExpiry.ExpiresAt);

            var expectedUnixExpiry = DateTimeOffset.FromUnixTimeSeconds(1_893_456_000);
            Environment.SetEnvironmentVariable(
                envName,
                """{"value":"expires-at-unix-token","expires_at":1893456000}""");
            var tokenWithUnixExpiry = await source.GetTokenAsync(CreateContext());

            Assert.NotNull(tokenWithUnixExpiry);
            Assert.Equal("expires-at-unix-token", tokenWithUnixExpiry!.Value);
            Assert.Equal(expectedUnixExpiry, tokenWithUnixExpiry.ExpiresAt);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    [Fact]
    public async Task GetTokenAsync_BadJson_ReturnsNullWithoutThrowing()
    {
        var envName = $"SHARP_TEST_BEARER_{Guid.NewGuid():N}";
        var source = new EnvironmentVariableBearerTokenSource([envName], fallbackToken: "fallback-token");

        try
        {
            Environment.SetEnvironmentVariable(envName, "{\"access_token\":\"broken\"");
            var token = await source.GetTokenAsync(CreateContext());

            Assert.Null(token);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    private static LlmCredentialContext CreateContext()
        => new(
            new ModelDescriptor("openai", "gpt-5-mini", ProviderApiKind.OpenAiResponses),
            "https://api.openai.com/v1/");
}
