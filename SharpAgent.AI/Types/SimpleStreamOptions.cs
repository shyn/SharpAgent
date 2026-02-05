namespace SharpAgent.AI.Types;


public class SimpleStreamOptions : BaseStreamOptions
{
    ///"minimal" | "low" | "medium" | "high" | "xhigh";
    public ThinkingLevel? Reasoning { get; set; }
    public ThinkingBudget? ThinkingBudget { get; set; }
}
