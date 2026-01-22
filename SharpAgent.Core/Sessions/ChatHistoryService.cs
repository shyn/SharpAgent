using System.Text.Json;

namespace SharpAgent.Core.Sessions;

public sealed class ChatHistoryService
{
    private readonly string _historyFilePath;
    private readonly int _maxMessages;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ChatHistoryService(string workingDirectory, int maxMessages = 50)
    {
        _historyFilePath = Path.Combine(workingDirectory, "chat_history.jsonl");
        _maxMessages = maxMessages;
    }

    public async Task<List<Message>> LoadHistoryAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_historyFilePath))
            return new List<Message>();

        var messages = new List<TimestampedMessage>();
        await foreach (var line in File.ReadLinesAsync(_historyFilePath, ct))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var msg = JsonSerializer.Deserialize<TimestampedMessage>(line, JsonOptions);
                if (msg != null) messages.Add(msg);
            }
            catch { /* skip malformed lines */ }
        }

        // Take last N messages (truncation)
        var truncated = messages.TakeLast(_maxMessages).ToList();
        
        // Convert to Message records
        return truncated.Select(ToMessage).ToList();
    }

    public async Task AppendMessagesAsync(IEnumerable<Message> messages, CancellationToken ct = default)
    {
        await using var writer = new StreamWriter(_historyFilePath, append: true);
        foreach (var msg in messages)
        {
            var timestamped = FromMessage(msg);
            var json = JsonSerializer.Serialize(timestamped, JsonOptions);
            await writer.WriteLineAsync(json);
        }
    }

    private static Message ToMessage(TimestampedMessage tm)
    {
        var role = Enum.TryParse<Role>(tm.Role, ignoreCase: true, out var r) ? r : Role.User;
        var toolCalls = tm.ToolCalls?.Select(tc => new ToolCall(tc.Id, tc.Name, tc.Arguments)).ToList();
        return new Message(role, tm.Content, tm.ToolName, tm.ToolCallId, toolCalls, tm.Thinking);
    }

    private static TimestampedMessage FromMessage(Message msg)
    {
        var toolCalls = msg.ToolCalls?.Select(tc => new ToolCallData
        {
            Id = tc.Id,
            Name = tc.Name,
            Arguments = tc.Arguments
        }).ToList();

        return new TimestampedMessage
        {
            Timestamp = DateTimeOffset.UtcNow,
            Role = msg.Role.ToString(),
            Content = msg.Content,
            ToolName = msg.ToolName,
            ToolCallId = msg.ToolCallId,
            ToolCalls = toolCalls,
            Thinking = msg.Thinking
        };
    }
}
