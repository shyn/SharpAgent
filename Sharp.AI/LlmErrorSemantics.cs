using System.Text.Json;

namespace Sharp.AI;

internal static class LlmErrorSemantics
{
    private static readonly string[] ContextOverflowIndicators =
    [
        "maximum context length",
        "context length exceeded",
        "context window",
        "prompt is too long",
        "input is too long",
        "too many tokens",
        "token limit exceeded",
        "reduce the length",
        "request too large"
    ];

    public static bool TryCreateContextOverflowError(
        string providerName,
        int statusCode,
        string responseBody,
        out LlmErrorEvent error)
    {
        if (!LooksLikeContextOverflow(statusCode, responseBody))
        {
            error = null!;
            return false;
        }

        var detail = ExtractErrorDetail(responseBody);
        error = new LlmErrorEvent(
            $"{providerName} context window exceeded: {detail}",
            LlmErrorCategory.ContextOverflow,
            statusCode,
            Retryable: false);
        return true;
    }

    private static bool LooksLikeContextOverflow(int statusCode, string responseBody)
    {
        if (statusCode is not (400 or 413 or 422))
            return false;

        if (string.IsNullOrWhiteSpace(responseBody))
            return false;

        var normalized = responseBody.ToLowerInvariant();
        return ContextOverflowIndicators.Any(indicator => normalized.Contains(indicator, StringComparison.Ordinal));
    }

    private static string ExtractErrorDetail(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetString(root, "message", out var message))
                    return message;

                if (root.TryGetProperty("error", out var errorProperty))
                {
                    if (errorProperty.ValueKind == JsonValueKind.Object
                        && TryGetString(errorProperty, "message", out message))
                    {
                        return message;
                    }

                    if (errorProperty.ValueKind == JsonValueKind.String)
                        return errorProperty.GetString()!;
                }
            }
        }
        catch (JsonException)
        {
            // Fall back to the raw body text if not JSON.
        }

        return responseBody.Trim();
    }

    private static bool TryGetString(JsonElement obj, string propertyName, out string value)
    {
        value = string.Empty;
        if (!obj.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
