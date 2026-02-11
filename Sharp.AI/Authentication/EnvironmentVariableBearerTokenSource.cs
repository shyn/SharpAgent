using System.Globalization;
using System.Text.Json;

namespace Sharp.AI.Authentication;

public sealed class EnvironmentVariableBearerTokenSource : ILlmBearerTokenSource
{
    private static readonly string[] TokenFieldCandidates = ["token", "access_token", "value"];
    private readonly IReadOnlyList<string> _tokenEnvironmentVariableCandidates;
    private readonly string? _fallbackToken;

    public EnvironmentVariableBearerTokenSource(
        IReadOnlyList<string> tokenEnvironmentVariableCandidates,
        string? fallbackToken = null)
    {
        _tokenEnvironmentVariableCandidates = tokenEnvironmentVariableCandidates ?? [];
        _fallbackToken = fallbackToken;
    }

    public ValueTask<LlmBearerToken?> GetTokenAsync(
        LlmCredentialContext context,
        CancellationToken ct = default)
    {
        var rawToken = ResolveTokenValue();
        if (string.IsNullOrWhiteSpace(rawToken))
            return ValueTask.FromResult<LlmBearerToken?>(null);

        return ValueTask.FromResult(ParseToken(rawToken));
    }

    private string? ResolveTokenValue()
    {
        foreach (var name in _tokenEnvironmentVariableCandidates)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return _fallbackToken;
    }

    private static LlmBearerToken? ParseToken(string rawToken)
    {
        var trimmed = rawToken.Trim();
        if (!LooksLikeJsonObject(trimmed))
            return new LlmBearerToken(trimmed);

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!TryReadTokenValue(root, out var tokenValue))
                return null;

            if (!TryReadExpiresAt(root, out var expiresAt))
                return null;

            return new LlmBearerToken(tokenValue, expiresAt);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool LooksLikeJsonObject(string value)
        => value.Length > 0 && value[0] == '{';

    private static bool TryReadTokenValue(JsonElement root, out string tokenValue)
    {
        foreach (var field in TokenFieldCandidates)
        {
            if (!TryGetPropertyIgnoreCase(root, field, out var element))
                continue;

            if (element.ValueKind == JsonValueKind.String)
            {
                var candidate = element.GetString();
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    tokenValue = candidate.Trim();
                    return true;
                }
            }

            tokenValue = string.Empty;
            return false;
        }

        tokenValue = string.Empty;
        return false;
    }

    private static bool TryReadExpiresAt(JsonElement root, out DateTimeOffset? expiresAt)
    {
        if (TryGetPropertyIgnoreCase(root, "expires_at", out var expiresAtElement))
            return TryParseExpiresAt(expiresAtElement, out expiresAt);

        if (TryGetPropertyIgnoreCase(root, "expires_in", out var expiresInElement))
            return TryParseExpiresIn(expiresInElement, out expiresAt);

        expiresAt = null;
        return true;
    }

    private static bool TryParseExpiresAt(JsonElement element, out DateTimeOffset? expiresAt)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            {
                var raw = element.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    expiresAt = null;
                    return false;
                }

                if (DateTimeOffset.TryParse(
                        raw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var parsed))
                {
                    expiresAt = parsed;
                    return true;
                }

                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixStringSeconds))
                {
                    expiresAt = DateTimeOffset.FromUnixTimeSeconds(unixStringSeconds);
                    return true;
                }

                expiresAt = null;
                return false;
            }
            case JsonValueKind.Number:
            {
                if (element.TryGetInt64(out var unixSeconds))
                {
                    expiresAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                    return true;
                }

                if (element.TryGetDouble(out var unixDoubleSeconds) && double.IsFinite(unixDoubleSeconds))
                {
                    expiresAt = DateTimeOffset.FromUnixTimeSeconds(checked((long)Math.Truncate(unixDoubleSeconds)));
                    return true;
                }

                expiresAt = null;
                return false;
            }
            default:
                expiresAt = null;
                return false;
        }
    }

    private static bool TryParseExpiresIn(JsonElement element, out DateTimeOffset? expiresAt)
    {
        double seconds;
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
            {
                if (element.TryGetDouble(out var numberSeconds) && double.IsFinite(numberSeconds))
                {
                    seconds = numberSeconds;
                    break;
                }

                expiresAt = null;
                return false;
            }
            case JsonValueKind.String:
            {
                var raw = element.GetString();
                if (string.IsNullOrWhiteSpace(raw) ||
                    !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSeconds) ||
                    !double.IsFinite(parsedSeconds))
                {
                    expiresAt = null;
                    return false;
                }

                seconds = parsedSeconds;
                break;
            }
            default:
                expiresAt = null;
                return false;
        }

        expiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
        return true;
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
