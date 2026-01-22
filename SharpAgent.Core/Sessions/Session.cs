namespace SharpAgent.Core.Sessions;

/// <summary>
/// A chat session that maintains conversation history between a human and an agent.
/// </summary>
public sealed class Session : ISession
{
    private readonly List<Message> _messages = new();

    public Guid Id { get; }
    public DateTime CreatedAt { get; }
    public DateTime UpdatedAt { get; private set; }
    public string? Title { get; set; }
    public IReadOnlyList<Message> Messages => _messages;

    /// <summary>
    /// Creates a new session with a new unique ID.
    /// </summary>
    public Session()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>
    /// Creates a session with the specified values (used for deserialization).
    /// </summary>
    internal Session(Guid id, DateTime createdAt, DateTime updatedAt, string? title, IEnumerable<Message>? messages)
    {
        Id = id;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Title = title;
        if (messages != null)
        {
            _messages.AddRange(messages);
        }
    }

    public void AddMessage(Message message)
    {
        _messages.Add(message);
        UpdatedAt = DateTime.UtcNow;
    }

    public void AddMessages(IEnumerable<Message> messages)
    {
        _messages.AddRange(messages);
        UpdatedAt = DateTime.UtcNow;
    }
}
