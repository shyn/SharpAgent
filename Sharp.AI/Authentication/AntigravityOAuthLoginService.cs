using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sharp.AI.Authentication;

public sealed record AntigravityOAuthCredential(
    string AccessToken,
    string RefreshToken,
    long ExpiresAtUnixMilliseconds,
    string ProjectId,
    string? Email);

public sealed class AntigravityOAuthLoginService
{
    private const string UserInfoUrl = "https://www.googleapis.com/oauth2/v1/userinfo?alt=json";
    private readonly HttpClient _httpClient;

    public AntigravityOAuthLoginService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<AntigravityOAuthCredential> LoginAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var verifier = GenerateCodeVerifier();
        var challenge = GenerateCodeChallenge(verifier);
        var state = verifier;

        var authUri = BuildAuthorizationUri(challenge, state);

        await using var callbackServer = new LoopbackCallbackServer(
            AntigravityOAuthConstants.CallbackPort,
            AntigravityOAuthConstants.CallbackPath);
        callbackServer.Start();

        progress?.Report("Opening browser for Google sign-in...");
        OpenBrowser(authUri.AbsoluteUri);
        progress?.Report($"Waiting for OAuth callback on localhost:{AntigravityOAuthConstants.CallbackPort}...");

        OAuthCallbackResult callback;
        try
        {
            callback = await callbackServer.WaitForCodeAsync(TimeSpan.FromMinutes(5), ct);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("OAuth callback timeout. Please try login again.");
        }

        if (!string.Equals(callback.State, state, StringComparison.Ordinal))
            throw new InvalidOperationException("OAuth state mismatch.");

        progress?.Report("Exchanging authorization code...");
        var token = await ExchangeCodeAsync(callback.Code, verifier, ct);

        progress?.Report("Fetching account info...");
        var email = await TryGetUserEmailAsync(token.AccessToken, ct);

        progress?.Report("Discovering antigravity project...");
        var projectId = await DiscoverProjectAsync(token.AccessToken, ct);

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn).AddMinutes(-5).ToUnixTimeMilliseconds();
        return new AntigravityOAuthCredential(
            AccessToken: token.AccessToken,
            RefreshToken: token.RefreshToken,
            ExpiresAtUnixMilliseconds: expiresAt,
            ProjectId: projectId,
            Email: email);
    }

    public static string ToCredentialEnvelope(AntigravityOAuthCredential credential)
    {
        var payload = new Dictionary<string, object?>
        {
            ["access"] = credential.AccessToken,
            ["refresh"] = credential.RefreshToken,
            ["expires"] = credential.ExpiresAtUnixMilliseconds,
            ["projectId"] = credential.ProjectId
        };

        if (!string.IsNullOrWhiteSpace(credential.Email))
            payload["email"] = credential.Email;

        return JsonSerializer.Serialize(payload);
    }

    private static Uri BuildAuthorizationUri(string challenge, string state)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["client_id"] = AntigravityOAuthConstants.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = AntigravityOAuthConstants.RedirectUri,
            ["scope"] = string.Join(" ", AntigravityOAuthConstants.Scopes),
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        };

        var query = string.Join("&", parameters.Select(static pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}"));

        return new Uri($"{AntigravityOAuthConstants.AuthUrl}?{query}", UriKind.Absolute);
    }

    private async Task<TokenExchangeResult> ExchangeCodeAsync(string code, string verifier, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AntigravityOAuthConstants.TokenUrl)
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = AntigravityOAuthConstants.ClientId,
                    ["client_secret"] = AntigravityOAuthConstants.ClientSecret,
                    ["code"] = code,
                    ["grant_type"] = "authorization_code",
                    ["redirect_uri"] = AntigravityOAuthConstants.RedirectUri,
                    ["code_verifier"] = verifier
                })
        };

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var accessToken = TryReadString(root, "access_token");
        var refreshToken = TryReadString(root, "refresh_token");
        var expiresIn = TryReadInt(root, "expires_in");

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Token exchange response missing access_token.");
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("Token exchange response missing refresh_token.");
        if (expiresIn <= 0)
            throw new InvalidOperationException("Token exchange response has invalid expires_in.");

        return new TokenExchangeResult(accessToken, refreshToken, expiresIn);
    }

    private async Task<string?> TryGetUserEmailAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return TryReadString(doc.RootElement, "email");
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> DiscoverProjectAsync(string accessToken, CancellationToken ct)
    {
        const string metadataHeader =
            "{\"ideType\":\"IDE_UNSPECIFIED\",\"platform\":\"PLATFORM_UNSPECIFIED\",\"pluginType\":\"GEMINI\"}";
        const string payloadJson =
            "{\"metadata\":{\"ideType\":\"IDE_UNSPECIFIED\",\"platform\":\"PLATFORM_UNSPECIFIED\",\"pluginType\":\"GEMINI\"}}";

        foreach (var endpoint in AntigravityOAuthConstants.ProjectDiscoveryEndpoints)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{endpoint}/v1internal:loadCodeAssist");

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.TryAddWithoutValidation("User-Agent", "google-api-nodejs-client/9.15.1");
                request.Headers.TryAddWithoutValidation("X-Goog-Api-Client", "google-cloud-sdk vscode_cloudshelleditor/0.1");
                request.Headers.TryAddWithoutValidation("Client-Metadata", metadataHeader);
                request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                    continue;

                var body = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                var discovered = TryReadProjectId(doc.RootElement);
                if (!string.IsNullOrWhiteSpace(discovered))
                    return discovered!;
            }
            catch
            {
                // Ignore per-endpoint errors and continue.
            }
        }

        return AntigravityOAuthConstants.DefaultProjectId;
    }

    private static string? TryReadProjectId(JsonElement root)
    {
        if (!root.TryGetProperty("cloudaicompanionProject", out var projectElement))
            return null;

        if (projectElement.ValueKind == JsonValueKind.String)
        {
            var asString = projectElement.GetString();
            return string.IsNullOrWhiteSpace(asString) ? null : asString.Trim();
        }

        if (projectElement.ValueKind == JsonValueKind.Object &&
            projectElement.TryGetProperty("id", out var idElement) &&
            idElement.ValueKind == JsonValueKind.String)
        {
            var id = idElement.GetString();
            return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
        }

        return null;
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to open browser. Please open this URL manually: {url}", ex);
        }
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

    private static int TryReadInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return 0;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var parsed) => parsed,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => 0
        };
    }

    private sealed record TokenExchangeResult(string AccessToken, string RefreshToken, int ExpiresIn);
    private sealed record OAuthCallbackResult(string Code, string State);

    private sealed class LoopbackCallbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _expectedPath;
        private readonly int _port;
        private bool _started;

        public LoopbackCallbackServer(int port, string expectedPath)
        {
            _port = port;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _expectedPath = expectedPath;
        }

        public void Start()
        {
            if (_started)
                return;

            _listener.Start();
            _started = true;
        }

        public async Task<OAuthCallbackResult> WaitForCodeAsync(TimeSpan timeout, CancellationToken ct)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            try
            {
                while (true)
                {
                    using var client = await _listener.AcceptTcpClientAsync(timeoutCts.Token);
                    await using var stream = client.GetStream();
                    var requestTarget = await ReadRequestTargetAsync(stream, timeoutCts.Token);
                    if (requestTarget == null)
                    {
                        await WriteHtmlResponseAsync(stream, 400, "Bad Request", "<h1>Invalid request</h1>", timeoutCts.Token);
                        continue;
                    }

                    var callbackUri = new Uri($"http://localhost:{_port}{requestTarget}");
                    if (!string.Equals(callbackUri.AbsolutePath, _expectedPath, StringComparison.Ordinal))
                    {
                        await WriteHtmlResponseAsync(stream, 404, "Not Found", "<h1>Not Found</h1>", timeoutCts.Token);
                        continue;
                    }

                    var query = ParseQuery(callbackUri.Query);
                    if (query.TryGetValue("error", out var error) && !string.IsNullOrWhiteSpace(error))
                    {
                        await WriteHtmlResponseAsync(
                            stream,
                            400,
                            "Bad Request",
                            $"<h1>Authentication Failed</h1><p>{WebUtility.HtmlEncode(error)}</p>",
                            timeoutCts.Token);
                        throw new InvalidOperationException($"OAuth authorization failed: {error}");
                    }

                    if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code) ||
                        !query.TryGetValue("state", out var state) || string.IsNullOrWhiteSpace(state))
                    {
                        await WriteHtmlResponseAsync(
                            stream,
                            400,
                            "Bad Request",
                            "<h1>Authentication Failed</h1><p>Missing code or state.</p>",
                            timeoutCts.Token);
                        continue;
                    }

                    await WriteHtmlResponseAsync(
                        stream,
                        200,
                        "OK",
                        "<h1>Authentication Successful</h1><p>You can close this window.</p>",
                        timeoutCts.Token);
                    return new OAuthCallbackResult(code, state);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("OAuth callback timeout.");
            }
        }

        private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken ct)
        {
            var buffer = new byte[2048];
            var builder = new StringBuilder();

            while (builder.Length < 16384)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read == 0)
                    break;

                builder.Append(Encoding.ASCII.GetString(buffer, 0, read));
                if (builder.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    break;
            }

            if (builder.Length == 0)
                return null;

            var request = builder.ToString();
            var firstLineEnd = request.IndexOf("\r\n", StringComparison.Ordinal);
            var firstLine = firstLineEnd >= 0 ? request[..firstLineEnd] : request;
            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return null;

            return parts[1];
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(query))
                return result;

            var trimmed = query[0] == '?' ? query[1..] : query;
            foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var index = pair.IndexOf('=');
                if (index < 0)
                {
                    result[WebUtility.UrlDecode(pair)] = string.Empty;
                    continue;
                }

                var key = WebUtility.UrlDecode(pair[..index]);
                var value = WebUtility.UrlDecode(pair[(index + 1)..]);
                result[key] = value;
            }

            return result;
        }

        private static async Task WriteHtmlResponseAsync(
            NetworkStream stream,
            int statusCode,
            string statusText,
            string htmlBody,
            CancellationToken ct)
        {
            var bodyBytes = Encoding.UTF8.GetBytes($"<html><body>{htmlBody}</body></html>");
            var header = new StringBuilder()
                .Append("HTTP/1.1 ").Append(statusCode).Append(' ').Append(statusText).Append("\r\n")
                .Append("Content-Type: text/html; charset=utf-8\r\n")
                .Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n")
                .Append("Connection: close\r\n\r\n")
                .ToString();

            var headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes.AsMemory(0, headerBytes.Length), ct);
            await stream.WriteAsync(bodyBytes.AsMemory(0, bodyBytes.Length), ct);
        }

        public ValueTask DisposeAsync()
        {
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }
}
