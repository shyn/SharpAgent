using Sharp.Core.Configuration;

namespace Sharp.Core.Tests;

public sealed class ConfigValueResolverTests : IDisposable
{
    private static readonly object EnvironmentLock = new();

    public ConfigValueResolverTests()
    {
        ConfigValueResolver.ClearCache();
    }

    public void Dispose()
    {
        ConfigValueResolver.ClearCache();
    }

    [Fact]
    public void Resolve_Null_ReturnsNull()
    {
        Assert.Null(ConfigValueResolver.Resolve(null));
    }

    [Fact]
    public void Resolve_Empty_ReturnsNull()
    {
        Assert.Null(ConfigValueResolver.Resolve(""));
    }

    [Fact]
    public void Resolve_LiteralValue_ReturnsLiteral()
    {
        Assert.Equal("my-api-key", ConfigValueResolver.Resolve("my-api-key"));
    }

    [Fact]
    public void Resolve_ExistingEnvVar_ReturnsEnvValue()
    {
        WithEnvironmentVariables(
            new Dictionary<string, string?> { ["SHARP_TEST_RESOLVE_KEY"] = "env-value-123" },
            () =>
            {
                Assert.Equal("env-value-123", ConfigValueResolver.Resolve("SHARP_TEST_RESOLVE_KEY"));
            });
    }

    [Fact]
    public void Resolve_NonExistentEnvVar_ReturnsRawValue()
    {
        Assert.Equal(
            "SHARP_NONEXISTENT_VAR_ABCXYZ",
            ConfigValueResolver.Resolve("SHARP_NONEXISTENT_VAR_ABCXYZ"));
    }

    [Fact]
    public void Resolve_CommandEchoHello_ReturnsHello()
    {
        var result = ConfigValueResolver.Resolve("!echo hello");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Resolve_CommandEmptyOutput_ReturnsNull()
    {
        var result = ConfigValueResolver.Resolve("!printf ''");
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_CommandNonexistent_ReturnsNull()
    {
        var result = ConfigValueResolver.Resolve("!nonexistent-command-xyz-999");
        Assert.Null(result);
    }

    [Fact]
    public void ResolveHeaders_Null_ReturnsNull()
    {
        Assert.Null(ConfigValueResolver.ResolveHeaders(null));
    }

    [Fact]
    public void ResolveHeaders_Empty_ReturnsNull()
    {
        Assert.Null(ConfigValueResolver.ResolveHeaders(
            new Dictionary<string, string>()));
    }

    [Fact]
    public void ResolveHeaders_WithValues_ResolvesEach()
    {
        WithEnvironmentVariables(
            new Dictionary<string, string?> { ["SHARP_TEST_HDR_VAL"] = "resolved-hdr" },
            () =>
            {
                var headers = new Dictionary<string, string>
                {
                    ["X-Literal"] = "literal-value",
                    ["X-Env"] = "SHARP_TEST_HDR_VAL",
                    ["X-Cmd"] = "!echo cmd-value"
                };

                var result = ConfigValueResolver.ResolveHeaders(headers);

                Assert.NotNull(result);
                Assert.Equal("literal-value", result!["X-Literal"]);
                Assert.Equal("resolved-hdr", result["X-Env"]);
                Assert.Equal("cmd-value", result["X-Cmd"]);
            });
    }

    [Fact]
    public void ClearCache_ClearsCommandResults()
    {
        var first = ConfigValueResolver.Resolve("!echo cached");
        Assert.Equal("cached", first);

        ConfigValueResolver.ClearCache();

        var second = ConfigValueResolver.Resolve("!echo cached");
        Assert.Equal("cached", second);
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
