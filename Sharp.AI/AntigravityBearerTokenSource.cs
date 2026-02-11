using System.Globalization;
using System.Text.Json;

namespace Sharp.AI;

public sealed class AntigravityBearerTokenSource : ILlmBearerTokenSource, IDisposable
{
    private const string TokenEndpointOverrideEnv = "SHARP_ANTIGRAVITY_OAUTH_TOKEN_ENDPOINT";
    private const string TokenEndpointCompatOverrideEnv = "ANTIGRAVITY_OAUTH_TOKEN_ENDPOINT";

    private readonly Func<string?> _credentialResolver;
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;

    private RuntimeRefreshState? _runtimeState;

    public AntigravityBearerTokenSource(
        IReadOnlyList<string> tokenEnvironmentVariableCandidates,
        string? fallbackToken = null,
        HttpMessageHandler? httpHandler = null)
    {
        _credentialResolver = () => ResolveEnvironmentCredential(tokenEnvironmentVariableCandidates, fallbackToken);
        if (httpHandler == null)
        {
            _httpClient = new HttpClient();
            _disposeHttpClient = true;
        }
        else
        {
            _httpClient = new HttpClient(httpHandler, disposeHandler: true);
            _disposeHttpClient = true;
        }
    }

    public AntigravityBearerTokenSource(
        string? rawCredential,
        HttpMessageHandler? httpHandler = null)
    {
        _credentialResolver = () => rawCredential;
        if (httpHandler == null)
        {
            _httpClient = new HttpClient();
            _disposeHttpClient = true;
        }
        else
        {
            _httpClient = new HttpClient(httpHandler, disposeHandler: true);
            _disposeHttpClient = true;
        }
    }

    public async ValueTask<LlmBearerToken?> GetTokenAsync(
        LlmCredentialContext context,
        CancellationToken ct = default)
    {
        var rawCredential = _credentialResolver();
        var payload = MergeWithRuntimeState(AntigravityCredentialEnvelope.Parse(rawCredential));
        if (string.IsNullOrWhiteSpace(payload.AccessToken) && string.IsNullOrWhiteSpace(payload.RefreshToken))
            return null;

        var now = DateTimeOffset.UtcNow;
        if (ShouldRefresh(payload, now))
        {
            var refreshed = await TryRefreshTokenAsync(payload, ct);
            if (refreshed != null)
            {
                _runtimeState = refreshed;
                return new LlmBearerToken(refreshed.AccessToken, refreshed.ExpiresAt);
            }

            var isAccessMissing = string.IsNullOrWhiteSpace(payload.AccessToken);
            var isExpired = payload.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow.AddMinutes(1);
            if (isAccessMissing || isExpired)
            {
                throw new InvalidOperationException(
                    "Failed to refresh Google Antigravity OAuth token. Please re-authenticate and update credentials.");
            }
        }

        if (!string.IsNullOrWhiteSpace(payload.AccessToken))
            return new LlmBearerToken(payload.AccessToken!, payload.ExpiresAt);

        return null;
    }

    private static string? ResolveEnvironmentCredential(
        IReadOnlyList<string> tokenEnvironmentVariableCandidates,
        string? fallbackToken)
    {
        foreach (var name in tokenEnvironmentVariableCandidates)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return fallbackToken;
    }

    private static bool ShouldRefresh(AntigravityCredentialPayload payload, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(payload.RefreshToken))
            return false;

        if (string.IsNullOrWhiteSpace(payload.AccessToken))
            return true;

        if (payload.ExpiresAt is null)
            return false;

        return payload.ExpiresAt.Value <= now.AddMinutes(1);
    }

    private async Task<RuntimeRefreshState?> TryRefreshTokenAsync(
        AntigravityCredentialPayload payload,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payload.RefreshToken))
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, ResolveTokenEndpoint())
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = AntigravityOAuthConstants.ClientId,
                    ["client_secret"] = AntigravityOAuthConstants.ClientSecret,
                    ["refresh_token"] = payload.RefreshToken!,
                    ["grant_type"] = "refresh_token"
                })
        };

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var accessToken = TryReadString(root, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
                return null;

            var expiresIn = TryReadDouble(root, "expires_in");
            DateTimeOffset? expiresAt = expiresIn is > 0
                ? DateTimeOffset.UtcNow.AddSeconds(expiresIn.Value).AddMinutes(-5)
                : null;

            var refreshToken = TryReadString(root, "refresh_token") ?? payload.RefreshToken;
            return new RuntimeRefreshState(accessToken!, refreshToken, expiresAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ResolveTokenEndpoint()
    {
        var overrideValue = Environment.GetEnvironmentVariable(TokenEndpointOverrideEnv);
        if (string.IsNullOrWhiteSpace(overrideValue))
            overrideValue = Environment.GetEnvironmentVariable(TokenEndpointCompatOverrideEnv);

        return string.IsNullOrWhiteSpace(overrideValue)
            ? AntigravityOAuthConstants.TokenUrl
            : overrideValue.Trim();
    }

    private AntigravityCredentialPayload MergeWithRuntimeState(AntigravityCredentialPayload payload)
    {
        if (_runtimeState == null)
            return payload;

        if (!string.IsNullOrWhiteSpace(payload.AccessToken))
            return payload;

        return payload with
        {
            AccessToken = _runtimeState.AccessToken,
            RefreshToken = _runtimeState.RefreshToken,
            ExpiresAt = _runtimeState.ExpiresAt
        };
    }

    private static string? TryReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind != JsonValueKind.String)
            return null;

        var result = value.GetString();
        return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
    }

    private static double? TryReadDouble(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return number;

        return null;
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
            _httpClient.Dispose();
    }

    private sealed record RuntimeRefreshState(
        string AccessToken,
        string? RefreshToken,
        DateTimeOffset? ExpiresAt);
}
