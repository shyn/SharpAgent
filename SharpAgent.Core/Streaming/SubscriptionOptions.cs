using System.Threading.Channels;

namespace SharpAgent.Core.Streaming;

public sealed record SubscriptionOptions(
    long? StartSeq = null,
    int BufferCapacity = 512,
    BoundedChannelFullMode FullMode = BoundedChannelFullMode.DropOldest,
    bool RequireReliableDelivery = false);
