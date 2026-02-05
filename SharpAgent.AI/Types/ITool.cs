namespace SharpAgent.AI.Types;

public class ToolParamSchema
{
    public string Name { get; set; }
    public string Type { get; set; }
    public string Description { get; set; }
    public bool Required { get; set; }
    public List<string> EnumValues { get; set; }
}

public interface ITool
{
    string Name { get; }
    string Description { get; }
    List<ToolParamSchema> Params { get; }
    Task<string> ExecuteAsync(Dictionary<string, string> args);
}
