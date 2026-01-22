using SharpAgent.Core.Streaming;

namespace SharpAgent.Core;

public interface ILlmClient : IDisposable
{
    Task<LlmResponse> GetCompletionAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ITool> tools,
        CancellationToken ct = default);

    IAsyncEnumerable<LlmStreamEvent> StreamCompletionAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ITool> tools,
        CancellationToken ct = default);
}

public abstract record LlmStreamEvent;

public sealed record LlmTextDeltaEvent(string Text) : LlmStreamEvent;
public sealed record LlmThinkingStartedEvent() : LlmStreamEvent;
public sealed record LlmThinkingDeltaEvent(string Thinking) : LlmStreamEvent;
public sealed record LlmThinkingCompletedEvent(string FullThinking) : LlmStreamEvent;
public sealed record LlmToolUseStartedEvent(string Id, string Name) : LlmStreamEvent;
public sealed record LlmToolUseArgumentsDeltaEvent(string Id, string PartialJson) : LlmStreamEvent;
public sealed record LlmToolUseCompletedEvent(string Id) : LlmStreamEvent;
public sealed record LlmMessageCompletedEvent(string? FullText, string? FullThinking, IReadOnlyList<ToolCall>? ToolCalls) : LlmStreamEvent;
