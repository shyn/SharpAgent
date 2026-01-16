# SharpAgent Streaming Architecture

## Overview

SharpAgent implements a streaming architecture that enables real-time event delivery from LLM providers through the agent layer to multiple consumers. The design supports:

- **Real-time streaming** - Text appears as it's generated
- **Multi-subscriber fan-out** - Multiple consumers (UI, logging, persistence) without double-consuming
- **Replay & persistence** - Events are persisted and can be replayed from any sequence number
- **Cross-process streaming** - SSE-compatible for server-to-web-UI scenarios

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Console / Web UI                            │
│                    await foreach (var evt in ...)                   │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                             Agent                                   │
│                     RunStreamingAsync()                             │
│              IAsyncEnumerable<AgentStreamEvent>                     │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                           ILlmClient                                │
│                    StreamCompletionAsync()                          │
│               IAsyncEnumerable<LlmStreamEvent>                      │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
                ┌───────────────┴───────────────┐
                ▼                               ▼
┌───────────────────────────┐   ┌───────────────────────────┐
│     AnthropicClient       │   │      OpenAiClient         │
│    (SSE streaming)        │   │   (non-streaming*)        │
└───────────────────────────┘   └───────────────────────────┘

* OpenAI streaming can be added later
```

## Event Model

### Two-Layer Event Design

The architecture uses two distinct event layers to maintain separation of concerns:

#### 1. LLM Stream Events (`LlmStreamEvent`)

Low-level events emitted by LLM providers. These represent raw model output:

```csharp
public abstract record LlmStreamEvent;

public sealed record LlmTextDeltaEvent(string Text) : LlmStreamEvent;
public sealed record LlmToolUseStartedEvent(string Id, string Name) : LlmStreamEvent;
public sealed record LlmToolUseArgumentsDeltaEvent(string Id, string PartialJson) : LlmStreamEvent;
public sealed record LlmToolUseCompletedEvent(string Id) : LlmStreamEvent;
public sealed record LlmMessageCompletedEvent(string? FullText, IReadOnlyList<ToolCall>? ToolCalls) : LlmStreamEvent;
```

#### 2. Agent Stream Events (`AgentStreamEvent`)

High-level events emitted by the Agent. These represent agent actions including tool execution:

```csharp
public abstract record AgentStreamEvent;

public sealed record AgentStartedEvent(string Goal) : AgentStreamEvent;
public sealed record AgentTextDeltaEvent(string Text) : AgentStreamEvent;
public sealed record AgentToolUseStartedEvent(string ToolCallId, string ToolName) : AgentStreamEvent;
public sealed record AgentToolUseArgumentsDeltaEvent(string ToolCallId, string PartialJson) : AgentStreamEvent;
public sealed record AgentToolUseCompletedEvent(string ToolCallId) : AgentStreamEvent;
public sealed record AgentToolCallStartedEvent(string ToolCallId, string ToolName, string Arguments) : AgentStreamEvent;
public sealed record AgentToolCallCompletedEvent(string ToolCallId, string Result, bool IsError) : AgentStreamEvent;
public sealed record AgentCompletedEvent(string FinalAnswer) : AgentStreamEvent;
public sealed record AgentErrorEvent(string Message) : AgentStreamEvent;
```

### Event Lifecycle

```
AgentStartedEvent
    │
    ├── AgentTextDeltaEvent (0..n)     ← streaming text chunks
    │
    ├── AgentToolUseStartedEvent       ← model requests tool
    ├── AgentToolUseArgumentsDeltaEvent (0..n)
    ├── AgentToolUseCompletedEvent
    │
    ├── AgentToolCallStartedEvent      ← agent executes tool
    ├── AgentToolCallCompletedEvent
    │
    └── (loop back for next iteration)
    │
AgentCompletedEvent | AgentErrorEvent
```

## Core Components

### ILlmClient Interface

```csharp
public interface ILlmClient
{
    // Non-streaming (convenience wrapper)
    Task<LlmResponse> GetCompletionAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ITool> tools,
        CancellationToken ct = default);

    // Streaming
    IAsyncEnumerable<LlmStreamEvent> StreamCompletionAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ITool> tools,
        CancellationToken ct = default);
}
```

### Agent Streaming API

```csharp
public sealed class Agent : IAgent
{
    // Non-streaming (consumes streaming internally)
    public async Task<string> RunAsync(string goal, CancellationToken ct = default);

    // Streaming
    public async IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(
        string goal,
        CancellationToken ct = default);
}
```

### EventStream Hub (Fan-out)

The `EventStream` class provides multi-subscriber support with independent buffering:

```csharp
public interface IEventStream
{
    StreamId Id { get; }
    
    ValueTask PublishAsync(string type, object payload, string? correlationId = null, CancellationToken ct = default);
    
    IAsyncEnumerable<AgentEventEnvelope> SubscribeAsync(
        SubscriptionOptions? options = null,
        CancellationToken ct = default);
    
    void Complete();
}

public sealed record SubscriptionOptions(
    long? StartSeq = null,                  // Replay from sequence number
    int BufferCapacity = 512,               // Per-subscriber buffer
    BoundedChannelFullMode FullMode = BoundedChannelFullMode.DropOldest,
    bool RequireReliableDelivery = false);
```

Key features:
- **Single publish, multiple consume** - Events are published once and fanned out to all subscribers
- **Independent backpressure** - Each subscriber has its own bounded channel
- **Configurable overflow behavior** - Drop oldest, drop newest, or block

### Event Envelope

Events are wrapped in an envelope for serialization and replay:

```csharp
public sealed record AgentEventEnvelope(
    StreamId StreamId,
    long Seq,                    // Monotonically increasing sequence number
    DateTimeOffset TimestampUtc,
    string Type,                 // Event type name
    JsonElement Payload,         // Serialized event data
    string? CorrelationId = null,
    int Version = 1);
```

### NDJSON Event Store

Persistence layer for replay and auditing:

```csharp
public interface IEventStore
{
    ValueTask AppendAsync(AgentEventEnvelope evt, CancellationToken ct = default);
    
    IAsyncEnumerable<AgentEventEnvelope> ReadFromAsync(
        StreamId streamId, 
        long startSeq, 
        CancellationToken ct = default);
    
    ValueTask<long?> TryGetLastSeqAsync(StreamId streamId, CancellationToken ct = default);
}
```

Storage format: Newline-delimited JSON (NDJSON), one event per line.

## Usage Examples

### Basic Streaming Console

```csharp
await foreach (var evt in agent.RunStreamingAsync("What is 2+2?"))
{
    switch (evt)
    {
        case AgentTextDeltaEvent delta:
            Console.Write(delta.Text);  // Real-time text output
            break;
            
        case AgentToolCallStartedEvent toolStart:
            Console.WriteLine($"\n→ Calling: {toolStart.ToolName}");
            break;
            
        case AgentToolCallCompletedEvent toolComplete:
            Console.WriteLine($"✓ Result: {toolComplete.Result}");
            break;
            
        case AgentCompletedEvent:
            Console.WriteLine();
            break;
    }
}
```

### Multi-Subscriber with EventStream

```csharp
var stream = new EventStream(new StreamId("session-123"), store: eventStore);

// UI subscriber
_ = Task.Run(async () =>
{
    await foreach (var evt in stream.SubscribeAsync())
        RenderToUI(evt);
});

// Logging subscriber
_ = Task.Run(async () =>
{
    await foreach (var evt in stream.SubscribeAsync())
        logger.LogInformation("Event: {Type}", evt.Type);
});

// Publish events
await stream.PublishAsync("agent.text_delta", new LlmTextDelta("Hello"));
```

### Replay from Sequence

```csharp
// Reconnect and replay from last seen sequence
var options = new SubscriptionOptions(StartSeq: lastSeenSeq + 1);
await foreach (var evt in stream.SubscribeAsync(options))
{
    ProcessEvent(evt);
}
```

## SSE Endpoint (Future)

The architecture is designed to support Server-Sent Events for web clients:

```csharp
app.MapGet("/streams/{streamId}/events", async (string streamId, HttpContext ctx, long? startSeq) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    
    var stream = streamRegistry.Get(new StreamId(streamId));
    var opts = new SubscriptionOptions(StartSeq: startSeq);

    await foreach (var evt in stream.SubscribeAsync(opts, ctx.RequestAborted))
    {
        await ctx.Response.WriteAsync($"id: {evt.Seq}\n");
        await ctx.Response.WriteAsync($"event: {evt.Type}\n");
        await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(evt)}\n\n");
        await ctx.Response.Body.FlushAsync();
    }
});
```

Browser reconnection is automatic via `Last-Event-ID` header.

## Design Rationale

### Why `IAsyncEnumerable`?

- **Natural fit for streaming** - Pull-based model matches async iteration
- **Built-in cancellation** - `CancellationToken` propagates naturally
- **Backpressure** - Consumer controls pace; producer waits for consumer
- **Composable** - Works with LINQ operators via `System.Linq.Async`

### Why Two Event Layers?

- **Separation of concerns** - LLM clients don't know about tool execution
- **Provider abstraction** - UI doesn't need to understand Anthropic SSE semantics
- **Testability** - Each layer can be mocked independently

### Why Channels for Fan-out?

- **Built-in to .NET** - No external dependencies
- **Bounded buffers** - Prevents memory issues with slow subscribers
- **Configurable overflow** - DropOldest for UI, block for critical paths

## File Structure

```
SharpAgent.Core/
├── Agent.cs                          # RunStreamingAsync implementation
├── ILlmClient.cs                     # StreamCompletionAsync interface + LlmStreamEvent types
├── AnthropicClient.cs                # SSE streaming implementation
├── OpenAiClient.cs                   # Non-streaming (streaming TODO)
└── Streaming/
    ├── AgentStreamEvents.cs          # Agent-level event types
    ├── AgentEventEnvelope.cs         # Event wrapper for serialization
    ├── AgentEvents.cs                # Additional event payload types
    ├── IEventStream.cs               # Fan-out hub interface
    ├── EventStream.cs                # Channel-based fan-out implementation
    ├── IEventStore.cs                # Persistence interface
    ├── NdjsonEventStore.cs           # NDJSON file persistence
    └── SubscriptionOptions.cs        # Subscriber configuration
```

## Future Enhancements

1. **OpenAI Streaming** - Implement SSE streaming for OpenAI provider
2. **SQLite Store** - Indexed storage for large-scale replay
3. **WebSocket Transport** - Bidirectional communication for interactive agents
4. **Event Filtering** - Subscribe to specific event types only
5. **Metrics/Observability** - Track lag, throughput, subscriber health
