namespace SharpAgent.Core;

public interface IAgent
{
    Task<string> RunAsync(string goal, CancellationToken ct = default);
}
