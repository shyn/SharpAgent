using System.Text.Json;

namespace Sharp.Core.Configuration;

public sealed class OAuthCredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly Dictionary<string, OAuthCredentialEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetCredential(string providerId, out string credential)
    {
        credential = string.Empty;
        if (!_entries.TryGetValue(providerId, out var entry))
            return false;

        if (string.IsNullOrWhiteSpace(entry.Credential))
            return false;

        credential = entry.Credential;
        return true;
    }

    public void SetCredential(string providerId, string credential)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("Provider id cannot be empty.", nameof(providerId));
        if (string.IsNullOrWhiteSpace(credential))
            throw new ArgumentException("Credential cannot be empty.", nameof(credential));

        _entries[providerId] = new OAuthCredentialEntry
        {
            Credential = credential.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public static OAuthCredentialStore LoadFromFile(string path)
    {
        var store = new OAuthCredentialStore();

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return store;

        try
        {
            var json = File.ReadAllText(path);
            var document = JsonSerializer.Deserialize<OAuthCredentialStoreDocument>(json, JsonOptions);
            if (document?.Providers == null)
                return store;

            foreach (var (providerId, entry) in document.Providers)
            {
                if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(entry?.Credential))
                    continue;

                store._entries[providerId] = new OAuthCredentialEntry
                {
                    Credential = entry.Credential.Trim(),
                    UpdatedAt = entry.UpdatedAt
                };
            }
        }
        catch (JsonException)
        {
            // Ignore malformed auth store and behave as empty store.
        }
        catch (IOException)
        {
            // Ignore unreadable auth store and behave as empty store.
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore inaccessible auth store and behave as empty store.
        }

        return store;
    }

    public void SaveToFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty.", nameof(path));

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var document = new OAuthCredentialStoreDocument
        {
            Version = 1,
            Providers = _entries.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase)
        };

        var json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(path, json);
    }

    private sealed class OAuthCredentialStoreDocument
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, OAuthCredentialEntry> Providers { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class OAuthCredentialEntry
    {
        public string Credential { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
