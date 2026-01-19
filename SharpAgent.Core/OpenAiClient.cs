using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SharpAgent.Core;

public sealed class OpenAiClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<OpenAiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public OpenAiClient(HttpClient httpClient, string model = "gpt-4o-mini", ILogger<OpenAiClient>? logger = null)
    {
        _httpClient = httpClient;
        _model = model;
        _logger = logger ?? NullLogger<OpenAiClient>.Instance;
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
        var response = await GetCompletionAsync(messages, tools, ct);
        
        if (!string.IsNullOrEmpty(response.Content))
        {
            yield return new LlmTextDeltaEvent(response.Content);
        }
        
        if (response.ToolCalls is { Count: > 0 })
        {
            foreach (var tc in response.ToolCalls)
            {
                yield return new LlmToolUseStartedEvent(tc.Id, tc.Name);
                yield return new LlmToolUseArgumentsDeltaEvent(tc.Id, tc.Arguments);
                yield return new LlmToolUseCompletedEvent(tc.Id);
            }
        }
        
        yield return new LlmMessageCompletedEvent(response.Content, null, response.ToolCalls);
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
}
