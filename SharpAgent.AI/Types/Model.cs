namespace SharpAgent.AI.Types;

public class Model
{
public string Id {get; set;}
public string Name { get; set; }
public string Api {get;set;}
	public string provider {get;set;}
	public string BaseUrl {get;set;}
	public bool Reasoning {get;set;}
	public InputType input {get;set;}
	public Cost cost {get;set;}
	public int ContextWindow {get;set;}
	public int MaxTokens {get;set;}
	public Dictionary<string, string> Headers {get;set;}
	/** Compatibility overrides for OpenAI-compatible APIs. If not set, auto-detected from baseUrl. */
	public OpenAICompletionsCompat? OpenAICompletionsCompat {get;set;}
	public OpenAIResponsesCompat? OpenAIResponsesCompat {get;set;}
}
