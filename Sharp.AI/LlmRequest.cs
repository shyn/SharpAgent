namespace Sharp.AI;

public sealed record LlmRequest(
    ModelDescriptor Model,
    string? SystemPrompt,
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<ToolDefinition> Tools,
    ThinkingLevel ThinkingLevel = ThinkingLevel.Off,
    int? MaxOutputTokens = null,
    string? SessionId = null,
    int? MaxRetryDelayMs = 60000,
    IReadOnlyDictionary<string, string>? Headers = null,
    Action<System.Text.Json.JsonElement>? OnPayload = null,
    ThinkingBudgets? ThinkingBudgets = null,
    Action<string>? OnDebugLog = null);
