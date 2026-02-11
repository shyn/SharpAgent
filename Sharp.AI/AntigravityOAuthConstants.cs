using System.Text;

namespace Sharp.AI;

internal static class AntigravityOAuthConstants
{
    internal const string DefaultProjectId = "rising-fact-p41fc";
    internal const string AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    internal const string TokenUrl = "https://oauth2.googleapis.com/token";
    internal const string RedirectUri = "http://localhost:51121/oauth-callback";
    internal const string CallbackPath = "/oauth-callback";
    internal const int CallbackPort = 51121;

    private const string ClientIdBase64 =
        "MTA3MTAwNjA2MDU5MS10bWhzc2luMmgyMWxjcmUyMzV2dG9sb2poNGc0MDNlcC5hcHBzLmdvb2dsZXVzZXJjb250ZW50LmNvbQ==";
    private const string ClientSecretBase64 = "R09DU1BYLUs1OEZXUjQ4NkxkTEoxbUxCOHNYQzR6NnFEQWY=";

    internal static string ClientId => DecodeBase64(ClientIdBase64);
    internal static string ClientSecret => DecodeBase64(ClientSecretBase64);

    internal static readonly string[] Scopes =
    [
        "https://www.googleapis.com/auth/cloud-platform",
        "https://www.googleapis.com/auth/userinfo.email",
        "https://www.googleapis.com/auth/userinfo.profile",
        "https://www.googleapis.com/auth/cclog",
        "https://www.googleapis.com/auth/experimentsandconfigs"
    ];

    internal static readonly string[] ProjectDiscoveryEndpoints =
    [
        "https://cloudcode-pa.googleapis.com",
        "https://daily-cloudcode-pa.sandbox.googleapis.com"
    ];

    private static string DecodeBase64(string value)
        => Encoding.UTF8.GetString(Convert.FromBase64String(value));
}
