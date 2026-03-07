using System.Text.Json;
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
    private object? _cachedPayload;

    public T? GetPayload<T>() where T : class
    {
        if (_cachedPayload is T typedPayload)
        {
            return typedPayload;
        }

        var deserialized = Payload.Deserialize<T>(Sharp.AI.Infrastructure.JsonDefaults.Options);
        _cachedPayload = deserialized;
        return deserialized;
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

    public bool Equals(SessionEntryEnvelope? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;

        return Type == other.Type &&
               Id == other.Id &&
               ParentId == other.ParentId &&
               TimestampUtc == other.TimestampUtc &&
               EqualityComparer<JsonElement>.Default.Equals(Payload, other.Payload);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Id, ParentId, TimestampUtc, EqualityComparer<JsonElement>.Default.GetHashCode(Payload));
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
