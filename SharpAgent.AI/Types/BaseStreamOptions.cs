namespace SharpAgent.AI.Types;

public enum CacheRetention
{
    None,
    Short,
    Long
}

public class BaseStreamOptions
{
    public double? Temperature { get; set; }

    public int? MaxTokens { get; set; }

    public CancellationToken? Signal { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>
    /// Prompt cache retention preference. Providers map this to their supported values.
    /// Default: "short".
    /// </summary>
    public CacheRetention? CacheRetention { get; set; }

    /// <summary>
    /// Optional session identifier for providers that support session-based caching.
    /// Providers can use this to enable prompt caching, request routing, or other
    /// session-aware features. Ignored by providers that don't support it.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Optional callback for inspecting provider payloads before sending.
    /// </summary>
    public Action<object?>? OnPayload { get; set; }

    /// <summary>
    /// Optional custom HTTP headers to include in API requests.
    /// Merged with provider defaults; can override default headers.
    /// Not supported by all providers (e.g., AWS Bedrock uses SDK auth).
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Maximum delay in milliseconds to wait for a retry when the server requests a long wait.
    /// If the server's requested delay exceeds this value, the request fails immediately
    /// with an error containing the requested delay, allowing higher-level retry logic
    /// to handle it with user visibility.
    /// Default: 60000 (60 seconds). Set to 0 to disable the cap.
    /// </summary>
    public int? MaxRetryDelayMs { get; set; }
}
