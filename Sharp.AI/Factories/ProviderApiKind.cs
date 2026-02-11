namespace Sharp.AI.Factories;

public enum ProviderApiKind
{
    OpenAiChatCompletions,
    OpenAiResponses,
    AnthropicMessages,
    GoogleGeminiCli
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
