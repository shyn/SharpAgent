using System.Text.Json;
using System.Text.Json.Serialization;
using Sharp.AI;
using Sharp.AI.Infrastructure;

namespace Sharp.Core.Sessions;

public sealed record SessionHeader(
    string Type,
    int Version,
    string SessionId,
    string WorkingDirectory,
    DateTimeOffset TimestampUtc);

public sealed record SessionEntryEnvelope
{
    private object? _cachedPayload;

    [JsonConstructor]
    public SessionEntryEnvelope(
        string type,
        string id,
        string? parentId,
        DateTimeOffset timestampUtc,
        JsonElement payload)
    {
        Type = type;
        Id = id;
        ParentId = parentId;
        TimestampUtc = timestampUtc;
        Payload = payload;
    }

    private SessionEntryEnvelope(SessionEntryEnvelope original)
    {
        Type = original.Type;
        Id = original.Id;
        ParentId = original.ParentId;
        TimestampUtc = original.TimestampUtc;
        Payload = original.Payload;
        _cachedPayload = null;
    }

    public string Type { get; init; }
    public string Id { get; init; }
    public string? ParentId { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public JsonElement Payload { get; init; }

    public T? GetPayload<T>() where T : class
    {
        if (_cachedPayload is T cached)
        {
            return cached;
        }

        var deserialized = Payload.Deserialize<T>(JsonDefaults.Options);
        _cachedPayload = deserialized;
        return deserialized;
    }

    public void Deconstruct(out string type, out string id, out string? parentId, out DateTimeOffset timestampUtc, out JsonElement payload)
    {
        type = Type;
        id = Id;
        parentId = ParentId;
        timestampUtc = TimestampUtc;
        payload = Payload;
    }

    public bool Equals(SessionEntryEnvelope? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Type == other.Type &&
               Id == other.Id &&
               ParentId == other.ParentId &&
               TimestampUtc.Equals(other.TimestampUtc) &&
               EqualityComparer<JsonElement>.Default.Equals(Payload, other.Payload);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Id, ParentId, TimestampUtc, Payload.GetHashCode());
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
