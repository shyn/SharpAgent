using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpAgent.Core.Sessions;

/// <summary>
/// File-based session store using JSON format.
/// Each session is stored as a separate JSON file named {guid}.json.
/// </summary>
public sealed class JsonSessionStore : ISessionStore
{
    private readonly string _sessionsDirectory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Creates a new JsonSessionStore with the default sessions directory.
    /// Default: ~/.sharpagent/sessions/
    /// </summary>
    public JsonSessionStore() : this(GetDefaultSessionsDirectory())
    {
    }

    /// <summary>
    /// Creates a new JsonSessionStore with a custom sessions directory.
    /// </summary>
    public JsonSessionStore(string sessionsDirectory)
    {
        _sessionsDirectory = sessionsDirectory;
    }

    public static string GetDefaultSessionsDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".sharpagent", "sessions");
    }

    public Task<ISession> CreateAsync(CancellationToken ct = default)
    {
        var session = new Session();
        return Task.FromResult<ISession>(session);
    }

    public async Task<ISession?> LoadAsync(Guid sessionId, CancellationToken ct = default)
    {
        var filePath = GetSessionFilePath(sessionId);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            var dto = JsonSerializer.Deserialize<SessionDto>(json, JsonOptions);
            if (dto == null)
            {
                return null;
            }

            var messages = dto.Messages?.Select(m => new Message(
                m.Role,
                m.Content,
                m.ToolName,
                m.ToolCallId,
                m.ToolCalls?.Select(tc => new ToolCall(tc.Id, tc.Name, tc.Arguments)).ToList(),
                m.Thinking
            )).ToList();

            return new Session(dto.Id, dto.CreatedAt, dto.UpdatedAt, dto.Title, messages);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(ISession session, CancellationToken ct = default)
    {
        EnsureDirectoryExists();

        var dto = new SessionDto
        {
            Id = session.Id,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            Title = session.Title,
            Messages = session.Messages.Select(m => new MessageDto
            {
                Role = m.Role,
                Content = m.Content,
                ToolName = m.ToolName,
                ToolCallId = m.ToolCallId,
                ToolCalls = m.ToolCalls?.Select(tc => new ToolCallDto
                {
                    Id = tc.Id,
                    Name = tc.Name,
                    Arguments = tc.Arguments
                }).ToList(),
                Thinking = m.Thinking
            }).ToList()
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var filePath = GetSessionFilePath(session.Id);
        await File.WriteAllTextAsync(filePath, json, ct);
    }

    public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
    {
        var filePath = GetSessionFilePath(sessionId);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default)
    {
        var summaries = new List<SessionSummary>();

        if (!Directory.Exists(_sessionsDirectory))
        {
            return Task.FromResult<IReadOnlyList<SessionSummary>>(summaries);
        }

        foreach (var file in Directory.GetFiles(_sessionsDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var dto = JsonSerializer.Deserialize<SessionDto>(json, JsonOptions);
                if (dto != null)
                {
                    summaries.Add(new SessionSummary(dto.Id, dto.Title, dto.CreatedAt, dto.UpdatedAt));
                }
            }
            catch
            {
                // Skip files that can't be parsed
            }
        }

        return Task.FromResult<IReadOnlyList<SessionSummary>>(
            summaries.OrderByDescending(s => s.UpdatedAt).ToList());
    }

    private string GetSessionFilePath(Guid sessionId) =>
        Path.Combine(_sessionsDirectory, $"{sessionId}.json");

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_sessionsDirectory))
        {
            Directory.CreateDirectory(_sessionsDirectory);
        }
    }

    // DTOs for JSON serialization
    private sealed class SessionDto
    {
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? Title { get; set; }
        public List<MessageDto>? Messages { get; set; }
    }

    private sealed class MessageDto
    {
        public Role Role { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? ToolName { get; set; }
        public string? ToolCallId { get; set; }
        public List<ToolCallDto>? ToolCalls { get; set; }
        public string? Thinking { get; set; }
    }

    private sealed class ToolCallDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
    }
}
