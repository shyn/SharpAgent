namespace SharpAgent.Core.Sessions;

/// <summary>
/// Summary information about a session, used for listing.
/// </summary>
public sealed record SessionSummary(Guid Id, string? Title, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>
/// Persistence abstraction for session storage.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Creates and persists a new empty session.
    /// </summary>
    Task<ISession> CreateAsync(CancellationToken ct = default);

    /// <summary>
    /// Loads a session by its ID. Returns null if not found.
    /// </summary>
    Task<ISession?> LoadAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Persists the current state of a session.
    /// </summary>
    Task SaveAsync(ISession session, CancellationToken ct = default);

    /// <summary>
    /// Deletes a session by its ID.
    /// </summary>
    Task DeleteAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Lists all sessions with summary information.
    /// </summary>
    Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default);
}
