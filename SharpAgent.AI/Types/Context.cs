namespace SharpAgent.AI.Types;

public class Context
{
	public string? SystemPrompt {get;set;}
	public List<IMessage> messages {get;set;}
	public List<ITool> tools {get;set;}
}
