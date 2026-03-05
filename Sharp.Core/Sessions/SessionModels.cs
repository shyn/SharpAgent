using System.Text.Json;
using Sharp.AI;

namespace Sharp.Core.Sessions;

public sealed record SessionHeader(
    string Type,
    int Version,
    string SessionId,
    string WorkingDirectory,
    DateTimeOffset TimestampUtc);

// SessionEntryEnvelope is now in its own file

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
