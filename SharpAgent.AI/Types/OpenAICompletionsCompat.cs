namespace SharpAgent.AI.Types;

public class OpenAICompletionsCompat {
	/** Whether the provider supports the `store` field. Default: auto-detected from URL. */
	public bool? SupportsStore { get; set; }
	/** Whether the provider supports the `developer` role (vs `system`). Default: auto-detected from URL. */
	public bool? SupportsDeveloperRole { get; set; }
	/** Whether the provider supports `reasoning_effort`. Default: auto-detected from URL. */
	public bool? SupportsReasoningEffort { get; set; }
	/** Whether the provider supports `stream_options: { include_usage: true }` for token usage in streaming responses. Default: true. */
	public bool? SupportsUsageInStreaming { get; set; }
	/** Which field to use for max tokens. Default: auto-detected from URL. */
	public string? MaxTokensField { get; set; }
	/** Whether tool results require the `name` field. Default: auto-detected from URL. */
	public bool? RequiresToolResultName { get; set; }
	/** Whether a user message after tool results requires an assistant message in between. Default: auto-detected from URL. */
	public bool? RequiresAssistantAfterToolResult { get; set; }
	/** Whether thinking blocks must be converted to text blocks with <thinking> delimiters. Default: auto-detected from URL. */
	public bool? RequiresThinkingAsText { get; set; }
	/** Whether tool call IDs must be normalized to Mistral format (exactly 9 alphanumeric chars). Default: auto-detected from URL. */
	public bool? RequiresMistralToolIds { get; set; }
	/** Format for reasoning/thinking parameter. "openai" uses reasoning_effort, "zai" uses thinking: { type: "enabled" }, "qwen" uses enable_thinking: boolean. Default: "openai". */
	public string? ThinkingFormat { get; set; }
	/** Whether the provider supports the `strict` field in tool definitions. Default: true. */
	public bool? SupportsStrictMode { get; set; }
}
