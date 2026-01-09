namespace SharpAgent.Core;

public interface ILlmClient
{
    Task<LlmResponse> GetCompletionAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ITool> tools,
        CancellationToken ct = default);
}
