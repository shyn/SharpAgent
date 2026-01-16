namespace SharpAgent.Core.Streaming;

public interface IEventStream
{
    StreamId Id { get; }

    ValueTask PublishAsync(string type, object payload, string? correlationId = null, CancellationToken ct = default);

    IAsyncEnumerable<AgentEventEnvelope> SubscribeAsync(
        SubscriptionOptions? options = null,
        CancellationToken ct = default);

    void Complete();
}
