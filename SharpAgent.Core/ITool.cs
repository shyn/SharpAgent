namespace SharpAgent.Core;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    object ParametersSchema { get; }
    Task<string> ExecuteAsync(string input, CancellationToken ct = default);
}
