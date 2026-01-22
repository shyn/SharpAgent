namespace SharpAgent.Core.Sessions;

/// <summary>
/// Represents a chat session between a human and an agent.
/// A session maintains conversation history across multiple turns.
/// </summary>
public interface ISession
{
    /// <summary>
    /// Unique identifier for this session.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// When this session was created.
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// When this session was last updated (message added).
    /// </summary>
    DateTime UpdatedAt { get; }

    /// <summary>
    /// Optional user-defined title for this session.
    /// </summary>
    string? Title { get; set; }

    /// <summary>
    /// All messages in this conversation, in chronological order.
    /// </summary>
    IReadOnlyList<Message> Messages { get; }

    /// <summary>
    /// Adds a single message to the session.
    /// </summary>
    void AddMessage(Message message);

    /// <summary>
    /// Adds multiple messages to the session.
    /// </summary>
    void AddMessages(IEnumerable<Message> messages);
}
