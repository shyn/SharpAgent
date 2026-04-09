using System.Text.Json;
using System.Text.Json.Serialization;
using Sharp.AI;

namespace Sharp.Core.Sessions;

public sealed record SessionHeader(
    string Type,
    int Version,
    string SessionId,
    string WorkingDirectory,
    DateTimeOffset TimestampUtc);

public sealed record SessionEntryEnvelope(
    string Type,
    string Id,
    string? ParentId,
    DateTimeOffset TimestampUtc,
    JsonElement Payload)
{
    [JsonIgnore]
    internal object? CachedPayload { get; set; }

    public T? GetPayload<T>(JsonSerializerOptions? options = null)
    {
        if (CachedPayload is T typed)
            return typed;

        var deserialized = Payload.Deserialize<T>(options ?? JsonDefaults.Options);
        CachedPayload = deserialized;
        return deserialized;
    }
}

public sealed record MessageEntryPayload(LlmMessage Message);

public sealed record ModelChangeEntryPayload(string Provider, string ModelId);

public sealed record ThinkingChangeEntryPayload(ThinkingLevel ThinkingLevel);

public sealed record MetadataEntryPayload(string Key, string Value);

public sealed record CompactionEntryPayload(
    string Summary,
    string FirstKeptEntryId,
    int TokensBefore,
    JsonElement? Details = null,
    bool FromHook = false);

public sealed record BranchSummaryEntryPayload(
    string FromId,
    string Summary,
    JsonElement? Details = null,
    bool FromHook = false);

public sealed record CustomMessageEntryPayload(
    string CustomType,
    string Content,
    bool Display = true,
    JsonElement? Details = null);

public sealed record LabelEntryPayload(
    string TargetId,
    string? Label);
