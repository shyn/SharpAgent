using System.Text.Json;
using Sharp.Core.Configuration;

namespace Sharp.Core.Tests;

public sealed class AuthStoreTests : IDisposable
{
    private static readonly object EnvironmentLock = new();
    private readonly string _tempDir;

    public AuthStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"authstore-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        ConfigValueResolver.ClearCache();
    }

    public void Dispose()
    {
        ConfigValueResolver.ClearCache();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string AuthPath => Path.Combine(_tempDir, "auth.json");

    [Fact]
    public void Load_MissingFile_ReturnsEmptyStore()
    {
        var store = AuthStore.LoadFromFile(Path.Combine(_tempDir, "nonexistent.json"));
        Assert.Empty(store.List());
    }

    [Fact]
    public void Load_EmptyFile_ReturnsEmptyStore()
    {
        File.WriteAllText(AuthPath, "");
        var store = AuthStore.LoadFromFile(AuthPath);
        Assert.Empty(store.List());
    }

    [Fact]
    public void Load_ApiKeyLiteral_GetApiKeyResolvesLiteral()
    {
        var json = """
        {
          "anthropic": { "type": "api_key", "key": "sk-ant-123" }
        }
        """;
        File.WriteAllText(AuthPath, json);

        var store = AuthStore.LoadFromFile(AuthPath);
        Assert.Equal("sk-ant-123", store.GetApiKey("anthropic"));
    }

    [Fact]
    public void Load_ApiKeyEnvVar_GetApiKeyResolvesEnvVar()
    {
        var json = """
        {
          "openai": { "type": "api_key", "key": "SHARP_AUTH_TEST_KEY" }
        }
        """;
        File.WriteAllText(AuthPath, json);

        WithEnvironmentVariables(
            new Dictionary<string, string?> { ["SHARP_AUTH_TEST_KEY"] = "resolved-from-env" },
            () =>
            {
                var store = AuthStore.LoadFromFile(AuthPath);
                Assert.Equal("resolved-from-env", store.GetApiKey("openai"));
            });
    }

    [Fact]
    public void Load_ApiKeyCommand_GetApiKeyResolvesCommand()
    {
        var json = """
        {
          "custom": { "type": "api_key", "key": "!echo test-cmd-value" }
        }
        """;
        File.WriteAllText(AuthPath, json);

        var store = AuthStore.LoadFromFile(AuthPath);
        Assert.Equal("test-cmd-value", store.GetApiKey("custom"));
    }

    [Fact]
    public void Load_OAuth_GetApiKeyReturnsAccessToken()
    {
        var json = """
        {
          "google": {
            "type": "oauth",
            "access": "ya29.token",
            "refresh": "1//refresh",
            "expires": 1730000000000,
            "projectId": "proj-1",
            "email": "user@example.com"
          }
        }
        """;
        File.WriteAllText(AuthPath, json);

        var store = AuthStore.LoadFromFile(AuthPath);
        Assert.Equal("ya29.token", store.GetApiKey("google"));

        var cred = store.Get("google") as OAuthAuthCredential;
        Assert.NotNull(cred);
        Assert.Equal("1//refresh", cred!.Refresh);
        Assert.Equal(1730000000000, cred.Expires);
        Assert.Equal("proj-1", cred.ProjectId);
        Assert.Equal("user@example.com", cred.Email);
    }

    [Fact]
    public void SetAndSave_ProducesCorrectJson()
    {
        var store = AuthStore.LoadFromFile(AuthPath);
        store.Set("test-provider", new ApiKeyAuthCredential { Key = "sk-test" });

        var json = File.ReadAllText(AuthPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("test-provider", out var entry));
        Assert.Equal("api_key", entry.GetProperty("type").GetString());
        Assert.Equal("sk-test", entry.GetProperty("key").GetString());
    }

    [Fact]
    public void Load_LegacyFormat_BackwardCompatible()
    {
        var oauthCred = JsonSerializer.Serialize(new
        {
            access = "legacy-access",
            refresh = "legacy-refresh",
            expires = 9999999999
        });

        var legacyJson = JsonSerializer.Serialize(new
        {
            Version = 1,
            Providers = new Dictionary<string, object>
            {
                ["google-legacy"] = new { Credential = oauthCred, UpdatedAt = "2024-01-01T00:00:00Z" },
                ["simple-key"] = new { Credential = "raw-key-value", UpdatedAt = "2024-01-01T00:00:00Z" }
            }
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AuthPath, legacyJson);

        var store = AuthStore.LoadFromFile(AuthPath);

        var oauth = store.Get("google-legacy") as OAuthAuthCredential;
        Assert.NotNull(oauth);
        Assert.Equal("legacy-access", oauth!.Access);
        Assert.Equal("legacy-refresh", oauth.Refresh);

        var apiKey = store.Get("simple-key") as ApiKeyAuthCredential;
        Assert.NotNull(apiKey);
        Assert.Equal("raw-key-value", apiKey!.Key);
    }

    [Fact]
    public void Has_ReturnsTrueForExisting_FalseForMissing()
    {
        var json = """
        {
          "exists": { "type": "api_key", "key": "k" }
        }
        """;
        File.WriteAllText(AuthPath, json);

        var store = AuthStore.LoadFromFile(AuthPath);
        Assert.True(store.Has("exists"));
        Assert.False(store.Has("nope"));
    }

    [Fact]
    public void Remove_RemovesProviderAndSaves()
    {
        var json = """
        {
          "a": { "type": "api_key", "key": "k1" },
          "b": { "type": "api_key", "key": "k2" }
        }
        """;
        File.WriteAllText(AuthPath, json);

        var store = AuthStore.LoadFromFile(AuthPath);
        Assert.True(store.Remove("a"));
        Assert.False(store.Has("a"));
        Assert.True(store.Has("b"));

        var reloaded = AuthStore.LoadFromFile(AuthPath);
        Assert.False(reloaded.Has("a"));
        Assert.True(reloaded.Has("b"));
    }

    [Fact]
    public void Reload_PicksUpFileChanges()
    {
        var json1 = """{ "p1": { "type": "api_key", "key": "v1" } }""";
        File.WriteAllText(AuthPath, json1);

        var store = AuthStore.LoadFromFile(AuthPath);
        Assert.Equal("v1", store.GetApiKey("p1"));
        Assert.False(store.Has("p2"));

        var json2 = """
        {
          "p1": { "type": "api_key", "key": "v1-updated" },
          "p2": { "type": "api_key", "key": "v2" }
        }
        """;
        File.WriteAllText(AuthPath, json2);

        store.Reload();
        Assert.Equal("v1-updated", store.GetApiKey("p1"));
        Assert.Equal("v2", store.GetApiKey("p2"));
    }

    [Fact]
    public void GetApiKey_NonExistentProvider_ReturnsNull()
    {
        var store = AuthStore.LoadFromFile(Path.Combine(_tempDir, "empty.json"));
        Assert.Null(store.GetApiKey("nope"));
    }

    [Fact]
    public void Remove_NonExistentProvider_ReturnsFalse()
    {
        var store = AuthStore.LoadFromFile(Path.Combine(_tempDir, "empty.json"));
        Assert.False(store.Remove("nope"));
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
