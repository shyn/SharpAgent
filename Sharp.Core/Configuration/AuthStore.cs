using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sharp.Core.Configuration;

[JsonConverter(typeof(AuthCredentialConverter))]
public abstract record AuthCredential
{
    public abstract string Type { get; }
}

public sealed record ApiKeyAuthCredential : AuthCredential
{
    public override string Type => "api_key";
    public required string Key { get; init; }
}

public sealed record OAuthAuthCredential : AuthCredential
{
    public override string Type => "oauth";
    public string? Access { get; init; }
    public string? Refresh { get; init; }
    public long? Expires { get; init; }
    public string? ProjectId { get; init; }
    public string? Email { get; init; }
}

public sealed class AuthCredentialConverter : JsonConverter<AuthCredential>
{
    public override AuthCredential? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp))
            return null;

        var type = typeProp.GetString();
        return type switch
        {
            "api_key" => new ApiKeyAuthCredential
            {
                Key = root.TryGetProperty("key", out var k) ? k.GetString() ?? "" : ""
            },
            "oauth" => new OAuthAuthCredential
            {
                Access = root.TryGetProperty("access", out var a) ? a.GetString() : null,
                Refresh = root.TryGetProperty("refresh", out var r) ? r.GetString() : null,
                Expires = root.TryGetProperty("expires", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt64() : null,
                ProjectId = root.TryGetProperty("projectId", out var p) ? p.GetString() : null,
                Email = root.TryGetProperty("email", out var em) ? em.GetString() : null,
            },
            _ => null
        };
    }

    public override void Write(Utf8JsonWriter writer, AuthCredential value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value.Type);

        switch (value)
        {
            case ApiKeyAuthCredential apiKey:
                writer.WriteString("key", apiKey.Key);
                break;
            case OAuthAuthCredential oauth:
                if (oauth.Access is not null) writer.WriteString("access", oauth.Access);
                if (oauth.Refresh is not null) writer.WriteString("refresh", oauth.Refresh);
                if (oauth.Expires is not null) writer.WriteNumber("expires", oauth.Expires.Value);
                if (oauth.ProjectId is not null) writer.WriteString("projectId", oauth.ProjectId);
                if (oauth.Email is not null) writer.WriteString("email", oauth.Email);
                break;
        }

        writer.WriteEndObject();
    }
}

public sealed class AuthStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;
    private Dictionary<string, AuthCredential> _credentials = new(StringComparer.OrdinalIgnoreCase);

    private AuthStore(string path)
    {
        _path = path;
    }

    public static AuthStore LoadFromFile(string path)
    {
        var store = new AuthStore(path);
        store.LoadInternal();
        return store;
    }

    public AuthCredential? Get(string providerId)
    {
        return _credentials.TryGetValue(providerId, out var cred) ? cred : null;
    }

    public string? GetApiKey(string providerId)
    {
        if (!_credentials.TryGetValue(providerId, out var cred))
            return null;

        return cred switch
        {
            ApiKeyAuthCredential apiKey => ConfigValueResolver.Resolve(apiKey.Key),
            OAuthAuthCredential oauth => oauth.Access,
            _ => null
        };
    }

    public void Set(string providerId, AuthCredential credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(credential);
        _credentials[providerId] = credential;
        SaveToFile();
    }

    public bool Remove(string providerId)
    {
        if (!_credentials.Remove(providerId))
            return false;
        SaveToFile();
        return true;
    }

    public bool Has(string providerId) => _credentials.ContainsKey(providerId);

    public string[] List() => [.. _credentials.Keys];

    public void Reload() => LoadInternal();

    public void SaveToFile() => SaveToFile(_path);

    public void SaveToFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(_credentials, JsonOptions);
        File.WriteAllText(path, json);

        SetFilePermissions(path);
    }

    private void LoadInternal()
    {
        _credentials = new Dictionary<string, AuthCredential>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path))
            return;

        try
        {
            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json))
                return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return;

            if (root.TryGetProperty("version", out _) || root.TryGetProperty("Version", out _))
            {
                TryLoadLegacyFormat(root);
                return;
            }

            var flat = JsonSerializer.Deserialize<Dictionary<string, AuthCredential>>(json, JsonOptions);
            if (flat is not null)
            {
                foreach (var (key, value) in flat)
                {
                    if (value is not null)
                        _credentials[key] = value;
                }
            }
        }
        catch (JsonException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void TryLoadLegacyFormat(JsonElement root)
    {
        JsonElement providers;
        if (!root.TryGetProperty("providers", out providers) &&
            !root.TryGetProperty("Providers", out providers))
            return;

        if (providers.ValueKind != JsonValueKind.Object)
            return;

        foreach (var prop in providers.EnumerateObject())
        {
            var providerId = prop.Name;
            if (string.IsNullOrWhiteSpace(providerId))
                continue;

            string? credentialJson = null;
            if (prop.Value.TryGetProperty("credential", out var credEl) ||
                prop.Value.TryGetProperty("Credential", out credEl))
            {
                credentialJson = credEl.GetString();
            }

            if (string.IsNullOrWhiteSpace(credentialJson))
                continue;

            try
            {
                using var credDoc = JsonDocument.Parse(credentialJson);
                var credRoot = credDoc.RootElement;

                if (credRoot.ValueKind == JsonValueKind.Object &&
                    (credRoot.TryGetProperty("access", out _) ||
                     credRoot.TryGetProperty("refresh", out _) ||
                     credRoot.TryGetProperty("expires", out _)))
                {
                    _credentials[providerId] = new OAuthAuthCredential
                    {
                        Access = credRoot.TryGetProperty("access", out var a) ? a.GetString() : null,
                        Refresh = credRoot.TryGetProperty("refresh", out var r) ? r.GetString() : null,
                        Expires = credRoot.TryGetProperty("expires", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt64() : null,
                        ProjectId = credRoot.TryGetProperty("projectId", out var p) ? p.GetString() : null,
                        Email = credRoot.TryGetProperty("email", out var em) ? em.GetString() : null,
                    };
                }
                else
                {
                    _credentials[providerId] = new ApiKeyAuthCredential { Key = credentialJson };
                }
            }
            catch (JsonException)
            {
                _credentials[providerId] = new ApiKeyAuthCredential { Key = credentialJson };
            }
        }
    }

    private static void SetFilePermissions(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best-effort; ignore on platforms that don't support it.
        }
    }
}
