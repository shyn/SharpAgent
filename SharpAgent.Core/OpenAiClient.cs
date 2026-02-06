using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SharpAgent.Core;

public sealed class OpenAiClient : ILlmClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _model;
    private readonly ILogger<OpenAiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public OpenAiClient(HttpClient httpClient, string model = "gpt-4o-mini", ILogger<OpenAiClient>? logger = null, bool ownsHttpClient = false)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _model = model;
        _logger = logger ?? NullLogger<OpenAiClient>.Instance;
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
        var request = new OpenAiRequest
        {
            Model = _model,
            Messages = messages.Select(ToOpenAiMessage).ToList(),
            Tools = tools.Count > 0 ? tools.Select(ToOpenAiTool).ToList() : null
        };

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        _logger.LogDebug("HTTP Request Body:\n{RequestBody}", requestJson);

        var response = await _httpClient.PostAsJsonAsync("chat/completions", request, JsonOptions, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        _logger.LogDebug("HTTP Response Body:\n{ResponseBody}", responseBody);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("HTTP {StatusCode} from {Url}: {Body}",
                (int)response.StatusCode, _httpClient.BaseAddress + "chat/completions", responseBody);
            response.EnsureSuccessStatusCode();
        }

        var result = JsonSerializer.Deserialize<OpenAiResponse>(responseBody, JsonOptions);
        var choice = result?.Choices?.FirstOrDefault()?.Message;

        if (choice is null)
            return new LlmResponse(string.Empty);

        var toolCalls = choice.ToolCalls?.Select(tc => 
            new ToolCall(tc.Id, tc.Function.Name, tc.Function.Arguments)).ToList();

        return new LlmResponse(choice.Content, null, toolCalls);
    }

    public async IAsyncEnumerable<LlmStreamEvent> StreamCompletionAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ITool> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new OpenAiRequest
        {
            Model = _model,
            Messages = messages.Select(ToOpenAiMessage).ToList(),
            Tools = tools.Count > 0 ? tools.Select(ToOpenAiTool).ToList() : null,
            Stream = true
        };

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        _logger.LogDebug("HTTP Request Body:\n{RequestBody}", requestJson);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("HTTP {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
            response.EnsureSuccessStatusCode();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        var state = new StreamingState();

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            if (data == "[DONE]") break;

            await foreach (var evt in ProcessStreamChunkAsync(data, state))
            {
                yield return evt;
            }
        }

        yield return new LlmMessageCompletedEvent(
            state.TextBuilder.Length > 0 ? state.TextBuilder.ToString() : null,
            null,
            state.ToolCalls.Count > 0 ? state.ToolCalls : null);
    }

    private async IAsyncEnumerable<LlmStreamEvent> ProcessStreamChunkAsync(string data, StreamingState state)
    {
        OpenAiStreamChunk? chunk;
        try
        {
            chunk = JsonSerializer.Deserialize<OpenAiStreamChunk>(data, JsonOptions);
        }
        catch
        {
            yield break;
        }

        var delta = chunk?.Choices?.FirstOrDefault()?.Delta;
        if (delta == null) yield break;

        if (!string.IsNullOrEmpty(delta.Content))
        {
            state.TextBuilder.Append(delta.Content);
            yield return new LlmTextDeltaEvent(delta.Content);
        }

        if (delta.ToolCalls != null)
        {
            foreach (var tc in delta.ToolCalls)
            {
                if (tc.Index >= state.ToolCalls.Count)
                {
                    var newTool = new ToolCall(tc.Id ?? $"call_{state.ToolCalls.Count}", tc.Function?.Name ?? "", "");
                    state.ToolCalls.Add(newTool);
                    yield return new LlmToolUseStartedEvent(newTool.Id, newTool.Name);
                }

                if (!string.IsNullOrEmpty(tc.Function?.Arguments))
                {
                    var current = state.ToolCalls[tc.Index];
                    state.ToolCalls[tc.Index] = current with { Arguments = current.Arguments + tc.Function.Arguments };
                    yield return new LlmToolUseArgumentsDeltaEvent(current.Id, tc.Function.Arguments);
                }
            }
        }

        var finishReason = chunk?.Choices?.FirstOrDefault()?.FinishReason;
        if (finishReason == "tool_calls")
        {
            foreach (var tc in state.ToolCalls)
            {
                yield return new LlmToolUseCompletedEvent(tc.Id);
            }
        }

        await Task.CompletedTask;
    }

    private sealed class StreamingState
    {
        public System.Text.StringBuilder TextBuilder { get; } = new();
        public List<ToolCall> ToolCalls { get; } = [];
    }

    private static OpenAiMessage ToOpenAiMessage(Message m) => m.Role switch
    {
        Role.Tool => new OpenAiMessage { Role = "tool", Content = m.Content, ToolCallId = m.ToolCallId },
        Role.Assistant when m.ToolCalls is { Count: > 0 } => new OpenAiMessage
        {
            Role = "assistant",
            Content = string.IsNullOrEmpty(m.Content) ? null : m.Content,
            ToolCalls = m.ToolCalls.Select(tc => new OpenAiToolCall
            {
                Id = tc.Id,
                Type = "function",
                Function = new OpenAiFunctionCall { Name = tc.Name, Arguments = tc.Arguments }
            }).ToList()
        },
        _ => new OpenAiMessage { Role = m.Role.ToString().ToLowerInvariant(), Content = m.Content }
    };

    private static OpenAiTool ToOpenAiTool(ITool t) => new()
    {
        Type = "function",
        Function = new OpenAiFunction
        {
            Name = t.Name,
            Description = t.Description,
            Parameters = t.ParametersSchema
        }
    };

    private sealed class OpenAiRequest
    {
        public required string Model { get; init; }
        public required List<OpenAiMessage> Messages { get; init; }
        public List<OpenAiTool>? Tools { get; init; }
        public bool? Stream { get; init; }
    }

    private sealed class OpenAiMessage
    {
        public required string Role { get; init; }
        public string? Content { get; init; }
        public string? ToolCallId { get; init; }
        public List<OpenAiToolCall>? ToolCalls { get; init; }
    }

    private sealed class OpenAiTool
    {
        public required string Type { get; init; }
        public required OpenAiFunction Function { get; init; }
    }

    private sealed class OpenAiFunction
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required object Parameters { get; init; }
    }

    private sealed class OpenAiToolCall
    {
        public required string Id { get; init; }
        public string Type { get; init; } = "function";
        public required OpenAiFunctionCall Function { get; init; }
    }

    private sealed class OpenAiFunctionCall
    {
        public required string Name { get; init; }
        public required string Arguments { get; init; }
    }

    private sealed class OpenAiResponse
    {
        public List<OpenAiChoice>? Choices { get; init; }
    }

    private sealed class OpenAiChoice
    {
        public OpenAiMessage? Message { get; init; }
    }

    private sealed class OpenAiStreamChunk
    {
        public List<OpenAiStreamChoice>? Choices { get; init; }
    }

    private sealed class OpenAiStreamChoice
    {
        public OpenAiStreamDelta? Delta { get; init; }
        public string? FinishReason { get; init; }
    }

    private sealed class OpenAiStreamDelta
    {
        public string? Content { get; init; }
        public List<OpenAiStreamToolCall>? ToolCalls { get; init; }
    }

    private sealed class OpenAiStreamToolCall
    {
        public int Index { get; init; }
        public string? Id { get; init; }
        public OpenAiStreamFunction? Function { get; init; }
    }

    private sealed class OpenAiStreamFunction
    {
        public string? Name { get; init; }
        public string? Arguments { get; init; }
    }
}
