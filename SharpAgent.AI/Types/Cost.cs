namespace SharpAgent.AI.Types;

public class Cost
{
	public double Input { get; set; }
	public double Output { get; set; }
	public double CacheRead { get; set; }
	public double CacheWrite { get; set; }
}
