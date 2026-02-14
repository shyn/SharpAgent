using System.Text.Json.Nodes;
using Sharp.AI;
using Sharp.Core.Sessions;

namespace Sharp.Core.Tests.TestDoubles;

public static class PiFixtureSessionImporter
{
    public static async Task ImportJsonlAsync(
        string fixturePath,
        SessionManager manager,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixturePath);
        ArgumentNullException.ThrowIfNull(manager);

        var entries = await LoadJsonlAsync(fixturePath, ct);
        MigrateEntries(entries);

        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (IsType(entry, "session"))
                continue;

            var sourceId = GetString(entry, "id");
            if (string.IsNullOrWhiteSpace(sourceId))
                continue;

            var appendedId = await AppendEntryAsync(entry, manager, idMap, ct);
            if (!string.IsNullOrWhiteSpace(appendedId))
                idMap[sourceId] = appendedId;
        }
    }

    public static LlmMessage LoadAssistantMessage(string fixturePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixturePath);

        var json = File.ReadAllText(fixturePath);
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidDataException($"Invalid JSON fixture: {fixturePath}");

        var role = GetString(root, "role");
        if (!string.Equals(role, "assistant", StringComparison.Ordinal))
            throw new InvalidDataException($"Expected assistant role in fixture: {fixturePath}");

        var content = ConvertContent(root["content"]);
        return new LlmMessage(LlmMessageRole.Assistant, content);
    }

    private static async Task<List<JsonObject>> LoadJsonlAsync(string path, CancellationToken ct)
    {
        var entries = new List<JsonObject>();

        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var node = JsonNode.Parse(line)?.AsObject();
            if (node != null)
                entries.Add(node);
        }

        return entries;
    }

    private static void MigrateEntries(IReadOnlyList<JsonObject> entries)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string? previousId = null;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (IsType(entry, "session"))
            {
                entry["version"] = 2;
                continue;
            }

            var id = GetString(entry, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                id = GenerateShortId(ids);
                entry["id"] = id;
            }

            ids.Add(id);

            if (!entry.ContainsKey("parentId"))
                entry["parentId"] = previousId;

            previousId = id;

            if (!IsType(entry, "compaction"))
                continue;

            var firstKeptId = GetString(entry, "firstKeptEntryId");
            if (!string.IsNullOrWhiteSpace(firstKeptId))
            {
                entry.Remove("firstKeptEntryIndex");
                continue;
            }

            var firstKeptIndex = GetInt32(entry, "firstKeptEntryIndex");
            if (firstKeptIndex is >= 0 and < int.MaxValue && firstKeptIndex.Value < entries.Count)
            {
                var target = entries[firstKeptIndex.Value];
                if (!IsType(target, "session"))
                {
                    var targetId = GetString(target, "id");
                    if (!string.IsNullOrWhiteSpace(targetId))
                        entry["firstKeptEntryId"] = targetId;
                }
            }

            entry.Remove("firstKeptEntryIndex");
        }
    }

    private static async Task<string?> AppendEntryAsync(
        JsonObject entry,
        SessionManager manager,
        IReadOnlyDictionary<string, string> idMap,
        CancellationToken ct)
    {
        var type = GetString(entry, "type");
        if (string.IsNullOrWhiteSpace(type))
            return null;

        switch (type)
        {
            case "message":
                {
                    var message = ConvertMessage(entry);
                    if (message == null)
                        return null;

                    var appended = await manager.AppendMessageAsync(message, ct);
                    return appended.Id;
                }
            case "model_change":
                {
                    var provider = GetString(entry, "provider");
                    var modelId = GetString(entry, "modelId");
                    if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(modelId))
                        return null;

                    var appended = await manager.AppendModelChangeAsync(provider, modelId, ct);
                    return appended.Id;
                }
            case "thinking_level_change":
                {
                    var level = ParseThinkingLevel(GetString(entry, "thinkingLevel"));
                    var appended = await manager.AppendThinkingChangeAsync(level, ct);
                    return appended.Id;
                }
            case "compaction":
                {
                    var summary = GetString(entry, "summary");
                    var firstKeptId = GetString(entry, "firstKeptEntryId");
                    if (string.IsNullOrWhiteSpace(summary) || string.IsNullOrWhiteSpace(firstKeptId))
                        return null;

                    if (!idMap.TryGetValue(firstKeptId, out var mappedFirstKeptId))
                        mappedFirstKeptId = idMap.Values.FirstOrDefault();

                    if (string.IsNullOrWhiteSpace(mappedFirstKeptId))
                        return null;

                    var tokensBefore = GetInt32(entry, "tokensBefore") ?? 0;
                    var appended = await manager.AppendCompactionAsync(summary, mappedFirstKeptId, tokensBefore, ct: ct);
                    return appended.Id;
                }
            case "branch_summary":
                {
                    var fromId = GetString(entry, "fromId");
                    var summary = GetString(entry, "summary");
                    if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(summary))
                        return null;

                    if (!idMap.TryGetValue(fromId, out var mappedFromId))
                        mappedFromId = idMap.Values.FirstOrDefault();

                    if (string.IsNullOrWhiteSpace(mappedFromId))
                        return null;

                    var appended = await manager.AppendBranchSummaryAsync(mappedFromId, summary, ct: ct);
                    return appended.Id;
                }
            case "custom_message":
                {
                    var customType = GetString(entry, "customType") ?? "fixture";
                    var content = FlattenContent(entry["content"]);
                    var display = GetBoolean(entry, "display") ?? true;

                    var appended = await manager.AppendCustomMessageAsync(customType, content, display, ct: ct);
                    return appended.Id;
                }
            case "label":
                {
                    var targetId = GetString(entry, "targetId");
                    if (string.IsNullOrWhiteSpace(targetId))
                        return null;

                    if (!idMap.TryGetValue(targetId, out var mappedTargetId))
                        return null;

                    var label = GetString(entry, "label");
                    var appended = await manager.AppendLabelAsync(mappedTargetId, label, ct);
                    return appended.Id;
                }
            default:
                return null;
        }
    }

    private static LlmMessage? ConvertMessage(JsonObject entry)
    {
        if (!entry.TryGetPropertyValue("message", out var messageNode) || messageNode is not JsonObject message)
            return null;

        var role = GetString(message, "role");
        if (string.IsNullOrWhiteSpace(role))
            return null;

        return role switch
        {
            "system" => new LlmMessage(LlmMessageRole.System, ConvertContent(message["content"])),
            "user" => new LlmMessage(LlmMessageRole.User, ConvertContent(message["content"])),
            "assistant" => new LlmMessage(LlmMessageRole.Assistant, ConvertContent(message["content"])),
            "toolResult" => LlmMessage.ToolResult(
                GetString(message, "toolCallId") ?? string.Empty,
                GetString(message, "toolName") ?? "unknown",
                FlattenContent(message["content"]),
                GetBoolean(message, "isError") ?? false),
            "custom" => LlmMessage.UserText(FlattenContent(message["content"])),
            "hookMessage" => LlmMessage.UserText(FlattenContent(message["content"])),
            _ => LlmMessage.UserText(FlattenContent(message["content"]))
        };
    }

    private static IReadOnlyList<ContentBlock> ConvertContent(JsonNode? contentNode)
    {
        if (contentNode is JsonValue scalar && scalar.TryGetValue<string>(out var textScalar))
            return [new TextContentBlock(textScalar)];

        if (contentNode is not JsonArray array)
            return [new TextContentBlock(string.Empty)];

        var blocks = new List<ContentBlock>();
        foreach (var item in array)
        {
            if (item is JsonValue value && value.TryGetValue<string>(out var rawText))
            {
                blocks.Add(new TextContentBlock(rawText));
                continue;
            }

            if (item is not JsonObject block)
                continue;

            var type = GetString(block, "type");
            switch (type)
            {
                case "text":
                    {
                        var text = GetString(block, "text");
                        if (!string.IsNullOrEmpty(text))
                            blocks.Add(new TextContentBlock(text));
                        break;
                    }
                case "thinking":
                    {
                        var thinking = GetString(block, "thinking");
                        var signature = GetString(block, "signature");
                        if (!string.IsNullOrEmpty(thinking) || !string.IsNullOrEmpty(signature))
                            blocks.Add(new ThinkingContentBlock(thinking ?? string.Empty, signature));
                        break;
                    }
                case "toolCall":
                    {
                        var id = GetString(block, "id") ?? string.Empty;
                        var name = GetString(block, "name") ?? "unknown";
                        var signature = GetString(block, "signature");
                        var argumentsJson = block.TryGetPropertyValue("arguments", out var argsNode) && argsNode != null
                            ? argsNode.ToJsonString()
                            : "{}";
                        blocks.Add(new ToolCallContentBlock(id, name, argumentsJson, signature));
                        break;
                    }
                case "image":
                    {
                        var mimeType = GetString(block, "mediaType") ?? GetString(block, "mimeType") ?? "application/octet-stream";
                        var base64 = GetString(block, "data") ?? GetString(block, "base64") ?? string.Empty;
                        blocks.Add(new ImageContentBlock(mimeType, base64));
                        break;
                    }
            }
        }

        if (blocks.Count == 0)
            blocks.Add(new TextContentBlock(string.Empty));

        return blocks;
    }

    private static string FlattenContent(JsonNode? contentNode)
    {
        if (contentNode == null)
            return string.Empty;

        if (contentNode is JsonValue scalar && scalar.TryGetValue<string>(out var text))
            return text;

        if (contentNode is not JsonArray array)
            return contentNode.ToJsonString();

        var parts = new List<string>();
        foreach (var item in array)
        {
            if (item is JsonValue value && value.TryGetValue<string>(out var line))
            {
                parts.Add(line);
                continue;
            }

            if (item is not JsonObject block)
                continue;

            var type = GetString(block, "type");
            var textPart = type switch
            {
                "text" => GetString(block, "text"),
                "thinking" => GetString(block, "thinking"),
                _ => GetString(block, "text")
            };

            if (!string.IsNullOrWhiteSpace(textPart))
                parts.Add(textPart);
        }

        return string.Join('\n', parts);
    }

    private static ThinkingLevel ParseThinkingLevel(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "minimal" => ThinkingLevel.Minimal,
            "low" => ThinkingLevel.Low,
            "medium" => ThinkingLevel.Medium,
            "high" => ThinkingLevel.High,
            "xhigh" => ThinkingLevel.XHigh,
            _ => ThinkingLevel.Off
        };

    private static bool IsType(JsonObject entry, string expected)
        => string.Equals(GetString(entry, "type"), expected, StringComparison.Ordinal);

    private static string GenerateShortId(ISet<string> existingIds)
    {
        for (var i = 0; i < 100; i++)
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            if (existingIds.Add(id))
                return id;
        }

        return Guid.NewGuid().ToString("N");
    }

    private static string? GetString(JsonObject obj, string propertyName)
    {
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue value)
            return null;

        return value.TryGetValue<string>(out var text) ? text : null;
    }

    private static int? GetInt32(JsonObject obj, string propertyName)
    {
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue value)
            return null;

        if (value.TryGetValue<int>(out var intValue))
            return intValue;

        if (!value.TryGetValue<long>(out var longValue))
            return null;

        return longValue is >= int.MinValue and <= int.MaxValue
            ? (int)longValue
            : null;
    }

    private static bool? GetBoolean(JsonObject obj, string propertyName)
    {
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue value)
            return null;

        return value.TryGetValue<bool>(out var boolValue)
            ? boolValue
            : null;
    }
}
