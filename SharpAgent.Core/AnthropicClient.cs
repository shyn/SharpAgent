using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpAgent.Core.Configuration;

namespace SharpAgent.Core;

public sealed class AnthropicClient : ILlmClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly ThinkingConfig _thinkingConfig;
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
        ThinkingConfig? thinkingConfig = null,
        ILogger<AnthropicClient>? logger = null,
        bool ownsHttpClient = false)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _model = model;
        _maxTokens = maxTokens;
        _thinkingConfig = thinkingConfig ?? new ThinkingConfig();
        _logger = logger ?? NullLogger<AnthropicClient>.Instance;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
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

        return new LlmResponse(completed?.FullText, completed?.FullThinking, completed?.ToolCalls);
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
            Stream = true,
            Thinking = _thinkingConfig.Enabled 
                ? new AnthropicThinkingConfig { Type = "enabled", BudgetTokens = _thinkingConfig.BudgetTokens }
                : null
        };

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        _logger.LogDebug("HTTP Request Body:\n{RequestBody}", requestJson);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "messages")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };

        // Add interleaved thinking header if thinking is enabled
        if (_thinkingConfig.Enabled)
        {
            httpRequest.Headers.Add("anthropic-beta", "interleaved-thinking-2025-05-14");
        }

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var requestUrl = _httpClient.BaseAddress + "messages";

            const int maxChars = 500;
            string truncatedRequestJson = requestJson.Length > maxChars
                ? requestJson[..maxChars] + $"... ({requestJson.Length - maxChars} more chars)"
                : requestJson;
            string truncatedErrorBody = errorBody.Length > maxChars
                ? errorBody[..maxChars] + $"... ({errorBody.Length - maxChars} more chars)"
                : errorBody;

            Console.WriteLine();
            Console.WriteLine("=== HTTP Error ===");
            Console.WriteLine($"Status Code: {(int)response.StatusCode} ({response.StatusCode})");
            Console.WriteLine($"Request URL: {requestUrl}");
            Console.WriteLine();
            Console.WriteLine("=== Request Body ===");
            Console.WriteLine(truncatedRequestJson);
            Console.WriteLine();
            Console.WriteLine("=== Response Body ===");
            Console.WriteLine(truncatedErrorBody);
            Console.WriteLine("==================");
            Console.WriteLine();

            _logger.LogError("HTTP {StatusCode} error. Request URL: {RequestUrl}. Request Body: {RequestBody}. Response: {ResponseBody}",
                (int)response.StatusCode, requestUrl, truncatedRequestJson, truncatedErrorBody);
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
        var thinking = state.ThinkingBuilder.ToString();
        _logger.LogDebug("Streamed response complete. Content length: {Length}, Thinking length: {ThinkingLength}, ToolCalls: {Count}",
            content.Length, thinking.Length, state.ToolCalls.Count);

        yield return new LlmMessageCompletedEvent(
            string.IsNullOrEmpty(content) ? null : content,
            string.IsNullOrEmpty(thinking) ? null : thinking,
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
        public StringBuilder ThinkingBuilder { get; } = new();
        public List<ToolCall> ToolCalls { get; } = [];
        public int CurrentToolIndex { get; set; } = -1;
        public bool IsInThinkingBlock { get; set; } = false;
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
                    state.IsInThinkingBlock = false;
                    yield return new LlmToolUseStartedEvent(id, name);
                }
                else if (blockStart?.ContentBlock?.Type == "thinking")
                {
                    state.IsInThinkingBlock = true;
                    yield return new LlmThinkingStartedEvent();
                }
                else if (blockStart?.ContentBlock?.Type == "text")
                {
                    state.IsInThinkingBlock = false;
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
                else if (delta?.Delta?.Type == "thinking_delta")
                {
                    var thinking = delta.Delta.Thinking ?? string.Empty;
                    state.ThinkingBuilder.Append(thinking);
                    yield return new LlmThinkingDeltaEvent(thinking);
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
                if (state.IsInThinkingBlock)
                {
                    var fullThinking = state.ThinkingBuilder.ToString();
                    if (!string.IsNullOrEmpty(fullThinking))
                    {
                        yield return new LlmThinkingCompletedEvent(fullThinking);
                    }
                    state.IsInThinkingBlock = false;
                }
                else if (state.CurrentToolIndex >= 0 && state.ToolCalls.Count > 0)
                {
                    yield return new LlmToolUseCompletedEvent(state.ToolCalls[^1].Id);
                    state.CurrentToolIndex = -1;
                }
                break;
        }
    }

    private AnthropicMessage ToAnthropicMessage(Message m) => new()
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

    private object[] CreateAssistantContent(Message m)
    {
        var content = new List<object>();

        // Add thinking block if present
        if (!string.IsNullOrEmpty(m.Thinking))
        {
            content.Add(new AnthropicThinkingContent { Type = "thinking", Thinking = m.Thinking, Signature = "" });
        }

        if (!string.IsNullOrEmpty(m.Content))
        {
            content.Add(new AnthropicTextContent { Type = "text", Text = m.Content });
        }

        foreach (var tc in m.ToolCalls!)
        {
            JsonElement input;
            try
            {
                input = JsonSerializer.Deserialize<JsonElement>(tc.Arguments);
            }
            catch (JsonException)
            {
                _logger.LogWarning("LLM returned malformed JSON, using empty object as fallback");
                // LLM returned malformed JSON, use empty object as fallback
                input = JsonSerializer.Deserialize<JsonElement>("{}");
            }
            
            content.Add(new AnthropicToolUse
            {
                Type = "tool_use",
                Id = tc.Id,
                Name = tc.Name,
                Input = input
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
        public AnthropicThinkingConfig? Thinking { get; init; }
    }

    private sealed class AnthropicThinkingConfig
    {
        public required string Type { get; init; }
        public int BudgetTokens { get; init; }
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

    private sealed class AnthropicThinkingContent
    {
        public required string Type { get; init; }
        public required string Thinking { get; init; }
        public required string Signature { get; init; }
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
        public string? Thinking { get; init; }
        public string? PartialJson { get; init; }
    }
}
