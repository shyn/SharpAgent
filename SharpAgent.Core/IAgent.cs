using SharpAgent.Core.Streaming;

namespace SharpAgent.Core;

public interface IAgent
{
    /// <summary>
    /// Runs the agent with a single goal (new conversation).
    /// </summary>
    Task<string> RunAsync(string goal, CancellationToken ct = default);

    /// <summary>
    /// Runs the agent with a single goal, streaming events.
    /// </summary>
    IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(string goal, CancellationToken ct = default);

    /// <summary>
    /// Continues an existing conversation with a new user message.
    /// </summary>
    /// <param name="existingMessages">Previous messages in the conversation.</param>
    /// <param name="userMessage">The new user message to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Stream of agent events including new messages generated.</returns>
    IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(
        IReadOnlyList<Message> existingMessages,
        string userMessage,
        CancellationToken ct = default);
}
