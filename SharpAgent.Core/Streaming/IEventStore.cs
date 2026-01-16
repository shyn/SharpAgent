namespace SharpAgent.Core.Streaming;

public interface IEventStore
{
    ValueTask AppendAsync(AgentEventEnvelope evt, CancellationToken ct = default);
    IAsyncEnumerable<AgentEventEnvelope> ReadFromAsync(StreamId streamId, long startSeq, CancellationToken ct = default);
    ValueTask<long?> TryGetLastSeqAsync(StreamId streamId, CancellationToken ct = default);
}
