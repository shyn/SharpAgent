using System.Globalization;
using System.Text.Json;

namespace Sharp.AI;

internal sealed record AntigravityCredentialPayload(
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string? ProjectId);

internal static class AntigravityCredentialEnvelope
{
    public static string ResolveProjectId(string? rawApiKey)
    {
        var payload = Parse(rawApiKey);
        if (!string.IsNullOrWhiteSpace(payload.ProjectId))
            return payload.ProjectId!;

        return AntigravityOAuthConstants.DefaultProjectId;
    }

    public static bool TryExtractToken(string? rawApiKey, out string? token)
    {
        token = Parse(rawApiKey).AccessToken;
        return !string.IsNullOrWhiteSpace(token);
    }

    public static AntigravityCredentialPayload Parse(string? rawApiKey)
    {
        if (string.IsNullOrWhiteSpace(rawApiKey))
            return new AntigravityCredentialPayload(null, null, null, null);

        var trimmed = rawApiKey.Trim();
        if (!LooksLikeJsonObject(trimmed))
        {
            return new AntigravityCredentialPayload(
                AccessToken: trimmed,
                RefreshToken: null,
                ExpiresAt: null,
                ProjectId: null);
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new AntigravityCredentialPayload(
                    AccessToken: trimmed,
                    RefreshToken: null,
                    ExpiresAt: null,
                    ProjectId: null);
            }

            var root = doc.RootElement;
            var accessToken = TryReadString(root, "token")
                              ?? TryReadString(root, "access_token")
                              ?? TryReadString(root, "accessToken")
                              ?? TryReadString(root, "value")
                              ?? TryReadString(root, "access");

            var refreshToken = TryReadString(root, "refresh_token")
                               ?? TryReadString(root, "refreshToken")
                               ?? TryReadString(root, "refresh");

            var projectId = TryReadString(root, "projectId")
                            ?? TryReadString(root, "project_id");

            DateTimeOffset? expiresAt = null;
            if (TryGetPropertyIgnoreCase(root, "expires", out var expiresElement))
            {
                expiresAt = ParseExpiresValue(expiresElement);
            }
            else if (TryGetPropertyIgnoreCase(root, "expiresAt", out expiresElement)
                     || TryGetPropertyIgnoreCase(root, "expires_at", out expiresElement))
            {
                expiresAt = ParseExpiresValue(expiresElement);
            }
            else if (TryGetPropertyIgnoreCase(root, "expires_in", out expiresElement)
                     || TryGetPropertyIgnoreCase(root, "expiresIn", out expiresElement))
            {
                var expiresInSeconds = ParseNumeric(expiresElement);
                if (expiresInSeconds is > 0)
                    expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds.Value);
            }

            return new AntigravityCredentialPayload(accessToken, refreshToken, expiresAt, projectId);
        }
        catch (JsonException)
        {
            return new AntigravityCredentialPayload(null, null, null, null);
        }
        catch (FormatException)
        {
            return new AntigravityCredentialPayload(null, null, null, null);
        }
    }

    private static bool LooksLikeJsonObject(string value)
        => value.Length > 0 && value[0] == '{';

    private static string? TryReadString(JsonElement root, string propertyName)
    {
        if (TryGetPropertyIgnoreCase(root, propertyName, out var value))
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => Normalize(value.GetString()),
                JsonValueKind.Number => Normalize(value.GetRawText()),
                _ => null
            };
        }

        return null;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTimeOffset? ParseExpiresValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
            {
                var raw = value.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                if (DateTimeOffset.TryParse(
                        raw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var parsedDateTime))
                    return parsedDateTime;

                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedNumeric))
                    return null;

                return ParseEpoch(parsedNumeric);
            }
            case JsonValueKind.Number:
            {
                if (value.TryGetDouble(out var numeric) && double.IsFinite(numeric))
                    return ParseEpoch(numeric);
                return null;
            }
            default:
                return null;
        }
    }

    private static double? ParseNumeric(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number))
            return number;

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            && double.IsFinite(number))
        {
            return number;
        }

        return null;
    }

    private static DateTimeOffset? ParseEpoch(double value)
    {
        // Heuristic: values > 10^11 are milliseconds epoch (Date.now-style), otherwise seconds epoch.
        var asMilliseconds = value > 100_000_000_000d;
        var epoch = asMilliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Truncate(value))
            : DateTimeOffset.FromUnixTimeSeconds((long)Math.Truncate(value));
        return epoch;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.TryGetProperty(propertyName, out value))
            return true;

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
