using System.Text.Json;

namespace SharpAgent.Core.Streaming;

public readonly record struct StreamId(string Value);

public sealed record AgentEventEnvelope(
    StreamId StreamId,
    long Seq,
    DateTimeOffset TimestampUtc,
    string Type,
    JsonElement Payload,
    string? CorrelationId = null,
    int Version = 1);
