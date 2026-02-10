namespace Sharp.AI;

public enum ProviderApiKind
{
    OpenAiChatCompletions,
    AnthropicMessages
}

public enum ThinkingLevel
{
    Off,
    Minimal,
    Low,
    Medium,
    High,
    XHigh
}

public enum LlmMessageRole
{
    System,
    User,
    Assistant,
    Tool
}
