using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace SharpAgent.Core.Streaming;

public sealed class EventStream : IEventStream
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IEventStore? _store;
    private readonly ConcurrentDictionary<Guid, Subscriber> _subscribers = new();

    private long _seq;
    private bool _completed;

    public StreamId Id { get; }

    private sealed class Subscriber
    {
        public required Channel<AgentEventEnvelope> Channel { get; init; }
        public required SubscriptionOptions Options { get; init; }
    }

    public EventStream(StreamId id, JsonSerializerOptions? jsonOptions = null, IEventStore? store = null)
    {
        Id = id;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        _store = store;
    }

    public async ValueTask PublishAsync(string type, object payload, string? correlationId = null, CancellationToken ct = default)
    {
        if (_completed) return;

        var payloadElement = JsonSerializer.SerializeToElement(payload, _jsonOptions);

        var evt = new AgentEventEnvelope(
            StreamId: Id,
            Seq: Interlocked.Increment(ref _seq),
            TimestampUtc: DateTimeOffset.UtcNow,
            Type: type,
            Payload: payloadElement,
            CorrelationId: correlationId);

        if (_store is not null)
            await _store.AppendAsync(evt, ct);

        foreach (var kv in _subscribers)
        {
            var sub = kv.Value;
            var writer = sub.Channel.Writer;

            if (sub.Options.RequireReliableDelivery)
            {
                await writer.WriteAsync(evt, ct);
            }
            else
            {
                writer.TryWrite(evt);
            }
        }
    }

    public async IAsyncEnumerable<AgentEventEnvelope> SubscribeAsync(
        SubscriptionOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        options ??= new SubscriptionOptions();

        if (options.StartSeq is long start && _store is not null)
        {
            await foreach (var evt in _store.ReadFromAsync(Id, start, ct))
                yield return evt;
        }

        var channel = Channel.CreateBounded<AgentEventEnvelope>(new BoundedChannelOptions(options.BufferCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = options.FullMode
        });

        var key = Guid.NewGuid();
        _subscribers[key] = new Subscriber { Channel = channel, Options = options };

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
                yield return evt;
        }
        finally
        {
            _subscribers.TryRemove(key, out _);
            channel.Writer.TryComplete();
        }
    }

    public void Complete()
    {
        _completed = true;
        foreach (var kv in _subscribers)
        {
            kv.Value.Channel.Writer.TryComplete();
        }
    }
}
