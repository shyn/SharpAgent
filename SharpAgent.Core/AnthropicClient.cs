using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SharpAgent.Core;

public sealed class AnthropicClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly ILogger<AnthropicClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AnthropicClient(
        HttpClient httpClient,
        string model = "claude-sonnet-4-20250514",
        int maxTokens = 8192,
        ILogger<AnthropicClient>? logger = null)
    {
        _httpClient = httpClient;
        _model = model;
        _maxTokens = maxTokens;
        _logger = logger ?? NullLogger<AnthropicClient>.Instance;
    }

    public async Task<LlmResponse> GetCompletionAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ITool> tools,
        CancellationToken ct = default)
    {
        LlmMessageCompletedEvent? completed = null;
        await foreach (var evt in StreamCompletionAsync(messages, tools, ct))
        {
            if (evt is LlmMessageCompletedEvent msg)
                completed = msg;
        }

        return new LlmResponse(completed?.FullText, completed?.ToolCalls);
    }

    public async IAsyncEnumerable<LlmStreamEvent> StreamCompletionAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ITool> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var systemMessage = messages.FirstOrDefault(m => m.Role == Role.System)?.Content;
        var nonSystemMessages = messages.Where(m => m.Role != Role.System).ToList();

        var request = new AnthropicRequest
        {
            Model = _model,
            MaxTokens = _maxTokens,
            System = systemMessage,
            Messages = nonSystemMessages.Select(ToAnthropicMessage).ToList(),
            Tools = tools.Count > 0 ? tools.Select(ToAnthropicTool).ToList() : null,
            Stream = true
        };

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        _logger.LogDebug("HTTP Request Body:\n{RequestBody}", requestJson);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "messages")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("HTTP {StatusCode} from {Url}: {Body}",
                (int)response.StatusCode, _httpClient.BaseAddress + "messages", errorBody);
            response.EnsureSuccessStatusCode();
        }

        var state = new StreamState();

        await foreach (var chunk in ReadSseStreamAsync(response, ct))
        {
            foreach (var evt in ProcessStreamEventToEvents(chunk, state))
            {
                yield return evt;
            }
        }

        var content = state.TextBuilder.ToString();
        _logger.LogDebug("Streamed response complete. Content length: {Length}, ToolCalls: {Count}",
            content.Length, state.ToolCalls.Count);

        yield return new LlmMessageCompletedEvent(
            string.IsNullOrEmpty(content) ? null : content,
            state.ToolCalls.Count > 0 ? state.ToolCalls : null);
    }

    private async IAsyncEnumerable<SseEvent> ReadSseStreamAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? eventType = null;

        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            if (line.StartsWith("event: "))
            {
                eventType = line[7..];
            }
            else if (line.StartsWith("data: ") && eventType is not null)
            {
                var data = line[6..];
                if (!string.IsNullOrEmpty(data))
                {
                    yield return new SseEvent(eventType, data);
                }
                eventType = null;
            }
        }
    }

    private sealed class StreamState
    {
        public StringBuilder TextBuilder { get; } = new();
        public List<ToolCall> ToolCalls { get; } = [];
        public int CurrentToolIndex { get; set; } = -1;
    }

    private IEnumerable<LlmStreamEvent> ProcessStreamEventToEvents(SseEvent evt, StreamState state)
    {
        _logger.LogTrace("SSE Event: {Type}", evt.Type);

        switch (evt.Type)
        {
            case "content_block_start":
                var blockStart = JsonSerializer.Deserialize<ContentBlockStart>(evt.Data, JsonOptions);
                if (blockStart?.ContentBlock?.Type == "tool_use")
                {
                    state.CurrentToolIndex = blockStart.Index;
                    var id = blockStart.ContentBlock.Id ?? string.Empty;
                    var name = blockStart.ContentBlock.Name ?? string.Empty;
                    state.ToolCalls.Add(new ToolCall(id, name, string.Empty));
                    yield return new LlmToolUseStartedEvent(id, name);
                }
                break;

            case "content_block_delta":
                var delta = JsonSerializer.Deserialize<ContentBlockDelta>(evt.Data, JsonOptions);
                if (delta?.Delta?.Type == "text_delta")
                {
                    var text = delta.Delta.Text ?? string.Empty;
                    state.TextBuilder.Append(text);
                    yield return new LlmTextDeltaEvent(text);
                }
                else if (delta?.Delta?.Type == "input_json_delta" && state.ToolCalls.Count > 0)
                {
                    var partialJson = delta.Delta.PartialJson ?? string.Empty;
                    var lastTool = state.ToolCalls[^1];
                    state.ToolCalls[^1] = lastTool with
                    {
                        Arguments = lastTool.Arguments + partialJson
                    };
                    yield return new LlmToolUseArgumentsDeltaEvent(lastTool.Id, partialJson);
                }
                break;

            case "content_block_stop":
                if (state.CurrentToolIndex >= 0 && state.ToolCalls.Count > 0)
                {
                    yield return new LlmToolUseCompletedEvent(state.ToolCalls[^1].Id);
                    state.CurrentToolIndex = -1;
                }
                break;
        }
    }

    private static AnthropicMessage ToAnthropicMessage(Message m) => new()
    {
        Role = m.Role switch
        {
            Role.User => "user",
            Role.Assistant => "assistant",
            Role.Tool => "user",
            _ => "user"
        },
        Content = m.Role == Role.Tool
            ? new object[]
            {
                new AnthropicToolResult
                {
                    Type = "tool_result",
                    ToolUseId = m.ToolCallId!,
                    Content = m.Content
                }
            }
            : m.ToolCalls is { Count: > 0 }
                ? CreateAssistantContent(m)
                : new object[] { new AnthropicTextContent { Type = "text", Text = m.Content } }
    };

    private static object[] CreateAssistantContent(Message m)
    {
        var content = new List<object>();

        if (!string.IsNullOrEmpty(m.Content))
        {
            content.Add(new AnthropicTextContent { Type = "text", Text = m.Content });
        }

        foreach (var tc in m.ToolCalls!)
        {
            content.Add(new AnthropicToolUse
            {
                Type = "tool_use",
                Id = tc.Id,
                Name = tc.Name,
                Input = JsonSerializer.Deserialize<JsonElement>(tc.Arguments)
            });
        }

        return content.ToArray();
    }

    private static AnthropicTool ToAnthropicTool(ITool t) => new()
    {
        Name = t.Name,
        Description = t.Description,
        InputSchema = t.ParametersSchema
    };

    private sealed record SseEvent(string Type, string Data);

    private sealed class AnthropicRequest
    {
        public required string Model { get; init; }
        public int MaxTokens { get; init; }
        public string? System { get; init; }
        public required List<AnthropicMessage> Messages { get; init; }
        public List<AnthropicTool>? Tools { get; init; }
        public bool Stream { get; init; }
    }

    private sealed class AnthropicMessage
    {
        public required string Role { get; init; }
        public required object[] Content { get; init; }
    }

    private sealed class AnthropicTextContent
    {
        public required string Type { get; init; }
        public required string Text { get; init; }
    }

    private sealed class AnthropicToolUse
    {
        public required string Type { get; init; }
        public required string Id { get; init; }
        public required string Name { get; init; }
        public JsonElement Input { get; init; }
    }

    private sealed class AnthropicToolResult
    {
        public required string Type { get; init; }
        public required string ToolUseId { get; init; }
        public required string Content { get; init; }
    }

    private sealed class AnthropicTool
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required object InputSchema { get; init; }
    }

    private sealed class ContentBlockStart
    {
        public int Index { get; init; }
        public ContentBlock? ContentBlock { get; init; }
    }

    private sealed class ContentBlock
    {
        public string? Type { get; init; }
        public string? Id { get; init; }
        public string? Name { get; init; }
    }

    private sealed class ContentBlockDelta
    {
        public int Index { get; init; }
        public Delta? Delta { get; init; }
    }

    private sealed class Delta
    {
        public string? Type { get; init; }
        public string? Text { get; init; }
        public string? PartialJson { get; init; }
    }
}
