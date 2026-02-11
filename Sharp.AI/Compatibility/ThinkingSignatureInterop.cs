using System.Buffers;
using System.Text.Json;

namespace Sharp.AI.Compatibility;

internal static class ThinkingSignatureInterop
{
    public static bool TryNormalizeOpenAiReasoningItem(
        string signature,
        out JsonElement reasoningItem,
        out string? summaryText)
    {
        reasoningItem = default;
        summaryText = null;

        if (!TryParseJsonRoot(signature, out var root))
            return false;

        return TryNormalizeOpenAiReasoningRoot(root, out reasoningItem, out summaryText);
    }

    private static bool TryParseJsonRoot(string raw, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var parsed = doc.RootElement;

            if (parsed.ValueKind == JsonValueKind.String)
            {
                var nested = parsed.GetString();
                if (!string.IsNullOrWhiteSpace(nested) && TryParseJsonRoot(nested, out var nestedRoot))
                {
                    root = nestedRoot;
                    return true;
                }
            }

            root = parsed.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryNormalizeOpenAiReasoningRoot(
        JsonElement root,
        out JsonElement reasoningItem,
        out string? summaryText)
    {
        reasoningItem = default;
        summaryText = null;

        if (root.ValueKind != JsonValueKind.Object)
            return false;

        var candidate = root;
        if (root.TryGetProperty("reasoning", out var reasoningProperty)
            && reasoningProperty.ValueKind == JsonValueKind.Object)
        {
            candidate = reasoningProperty;
        }

        if (!LooksLikeOpenAiReasoning(candidate))
            return false;

        summaryText = ExtractSummaryText(candidate);

        var type = TryGetString(candidate, "type");
        if (string.Equals(type, "reasoning", StringComparison.OrdinalIgnoreCase))
        {
            reasoningItem = candidate.Clone();
            return true;
        }

        var normalized = EnsureReasoningType(candidate);
        reasoningItem = normalized;
        return true;
    }

    private static bool LooksLikeOpenAiReasoning(JsonElement candidate)
    {
        if (candidate.ValueKind != JsonValueKind.Object)
            return false;

        var type = TryGetString(candidate, "type");
        if (string.Equals(type, "reasoning", StringComparison.OrdinalIgnoreCase))
            return true;

        if (candidate.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Array)
            return true;

        if (candidate.TryGetProperty("encrypted_content", out _))
            return true;

        if (candidate.TryGetProperty("id", out _)
            && candidate.TryGetProperty("status", out _))
        {
            return true;
        }

        return false;
    }

    private static JsonElement EnsureReasoningType(JsonElement candidate)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "reasoning");
            foreach (var property in candidate.EnumerateObject())
            {
                if (property.NameEquals("type"))
                    continue;

                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(buffer.WrittenMemory);
        return doc.RootElement.Clone();
    }

    private static string? ExtractSummaryText(JsonElement candidate)
    {
        if (!candidate.TryGetProperty("summary", out var summary)
            || summary.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var item in summary.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            if (TryGetString(item, "text") is { } text && !string.IsNullOrWhiteSpace(text))
                parts.Add(text);
        }

        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    private static string? TryGetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var propertyValue)
            || propertyValue.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return propertyValue.GetString();
    }
}
