using System;
using System.Text.Json;
using Sharp.AI.Infrastructure;

namespace Sharp.Core.Sessions;

public sealed record SessionEntryEnvelope(
    string Type,
    string Id,
    string? ParentId,
    DateTimeOffset TimestampUtc,
    JsonElement Payload)
{
    private object? _cachedPayload;

    public T? GetPayload<T>(JsonSerializerOptions? options = null) where T : class
    {
        if (_cachedPayload is T typedPayload)
        {
            return typedPayload;
        }

        var deserialized = Payload.Deserialize<T>(options ?? JsonDefaults.Options);
        _cachedPayload = deserialized;
        return deserialized;
    }
}
