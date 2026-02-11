namespace Sharp.AI.Contracts;

public interface ILlmProvider : IDisposable
{
    string ProviderId { get; }

    IAsyncEnumerable<LlmStreamEvent> StreamAsync(LlmRequest request, CancellationToken ct = default);
}
