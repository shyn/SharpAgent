using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sharp.AI.Providers;

public sealed class OpenAiLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiLlmProvider> _logger;

    public OpenAiLlmProvider(HttpClient httpClient, ILogger<OpenAiLlmProvider>? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger ?? NullLogger<OpenAiLlmProvider>.Instance;
    }

    public string ProviderId => "openai";

    public void Dispose() => _httpClient.Dispose();

    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        void Debug(string message) => request.OnDebugLog?.Invoke(message);

        if (request.Model.ApiKind != ProviderApiKind.OpenAiChatCompletions)
        {
            yield return new LlmErrorEvent(
                $"Model '{request.Model.ModelId}' is not configured for OpenAI Chat Completions",
                LlmErrorCategory.Validation,
                Retryable: false);
            yield break;
        }

        var payload = new OpenAiRequest
        {
            Model = request.Model.ModelId,
            Messages = request.Messages.Select(ToOpenAiMessage).ToList(),
            Tools = request.Tools.Count > 0 ? request.Tools.Select(ToOpenAiTool).ToList() : null,
            Stream = true,
            StreamOptions = new OpenAiStreamOptions { IncludeUsage = true },
            MaxTokens = request.MaxOutputTokens ?? request.Model.MaxOutputTokens
        };

        var payloadElement = JsonSerializer.SerializeToElement(payload, JsonDefaults.Options);
        request.OnPayload?.Invoke(payloadElement);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(payloadElement.GetRawText(), Encoding.UTF8, "application/json")
        };

        var requestUrl = httpRequest.RequestUri == null
            ? new Uri(_httpClient.BaseAddress ?? new Uri("http://localhost/"), "chat/completions")
            : httpRequest.RequestUri;

        if (!string.IsNullOrWhiteSpace(request.SessionId))
            httpRequest.Headers.TryAddWithoutValidation("x-session-id", request.SessionId);

        ApplyHeaders(httpRequest, request.Headers);
        Debug($"request.url={requestUrl}");
        Debug($"request.messages={request.Messages.Count} tools={request.Tools.Count}");
        Debug($"request.headers={FormatRequestHeaders(httpRequest)}");
        Debug($"request.payload={Truncate(payloadElement.GetRawText())}");

        HttpResponseMessage? response = null;
        LlmErrorEvent? transportError = null;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            transportError = new LlmErrorEvent("OpenAI request was aborted", LlmErrorCategory.Aborted, Retryable: true);
        }
        catch (TaskCanceledException)
        {
            transportError = new LlmErrorEvent("OpenAI request timed out", LlmErrorCategory.Timeout, Retryable: true);
        }
        catch (HttpRequestException ex)
        {
            transportError = new LlmErrorEvent($"OpenAI network error: {ex.Message}", LlmErrorCategory.Network, Retryable: true);
        }

        if (transportError != null)
        {
            Debug($"transport.error={transportError.Message} category={transportError.Category}");
            yield return transportError;
            yield break;
        }

        var safeResponse = response!;
        using (safeResponse)
        {
            Debug($"response.status={(int)safeResponse.StatusCode}");
            Debug($"response.headers={FormatResponseHeaders(safeResponse)}");

            if (!safeResponse.IsSuccessStatusCode)
            {
                var body = await safeResponse.Content.ReadAsStringAsync(ct);
                var statusCode = (int)safeResponse.StatusCode;
                var category = ClassifyStatusCode(statusCode);
                var retryable = IsRetryableStatusCode(statusCode);
                Debug($"response.body={Truncate(body)}");

                if (request.MaxRetryDelayMs is > 0
                    && TryGetRetryAfterSeconds(safeResponse, out var retryAfterSeconds)
                    && retryAfterSeconds * 1000 > request.MaxRetryDelayMs)
                {
                    yield return new LlmErrorEvent(
                        $"OpenAI requested retry-after {retryAfterSeconds}s, above cap {request.MaxRetryDelayMs}ms",
                        LlmErrorCategory.RateLimit,
                        statusCode,
                        Retryable: true);
                    yield break;
                }

                _logger.LogError("OpenAI stream request failed: HTTP {Status} {Body}", statusCode, body);
                yield return new LlmErrorEvent(
                    $"OpenAI request failed with HTTP {statusCode}: {body}",
                    category,
                    statusCode,
                    retryable);
                yield break;
            }

            await using var stream = await safeResponse.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            var state = new StreamState();

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                Debug($"response.line={Truncate(line, 4096)}");

                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal))
                    continue;

                var data = line[6..];
                if (data == "[DONE]")
                {
                    Debug("response.done=true");
                    break;
                }

                foreach (var evt in ProcessChunk(data, state))
                {
                    Debug($"response.event={DescribeStreamEvent(evt)}");
                    yield return evt;
                }
            }

            foreach (var toolState in state.ToolCalls.Values.OrderBy(x => x.Index))
            {
                if (state.CompletedToolIds.Add(toolState.Id))
                    yield return new LlmToolUseCompletedEvent(toolState.Id);
            }

            yield return new LlmCompletedEvent(
                state.TextBuilder.Length == 0 ? null : state.TextBuilder.ToString(),
                null,
                state.ToolCalls.Values
                    .OrderBy(x => x.Index)
                    .Select(x => new ToolCall(x.Id, x.Name, x.ArgumentsBuilder.ToString()))
                    .ToList(),
                state.Usage);
            Debug($"response.completed text_chars={state.TextBuilder.Length} tool_calls={state.ToolCalls.Count}");
        }
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers == null)
            return;

        foreach (var header in headers)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
    }

    private static bool TryGetRetryAfterSeconds(HttpResponseMessage response, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter == null)
            return false;

        if (retryAfter.Delta.HasValue)
        {
            retryAfterSeconds = Math.Max(0, (int)retryAfter.Delta.Value.TotalSeconds);
            return true;
        }

        if (retryAfter.Date.HasValue)
        {
            retryAfterSeconds = Math.Max(0, (int)(retryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds);
            return true;
        }

        return false;
    }

    private static LlmErrorCategory ClassifyStatusCode(int statusCode)
    {
        return statusCode switch
        {
            408 => LlmErrorCategory.Timeout,
            429 => LlmErrorCategory.RateLimit,
            >= 500 => LlmErrorCategory.Server,
            _ => LlmErrorCategory.Validation
        };
    }

    private static bool IsRetryableStatusCode(int statusCode)
        => statusCode is 408 or 429 or >= 500;

    private static string DescribeStreamEvent(LlmStreamEvent evt)
    {
        return evt switch
        {
            LlmTextDeltaEvent text => $"text_delta chars={text.Delta.Length}",
            LlmThinkingStartedEvent => "thinking_start",
            LlmThinkingDeltaEvent thinking => $"thinking_delta chars={thinking.Delta.Length}",
            LlmThinkingCompletedEvent thinkingCompleted => $"thinking_end chars={thinkingCompleted.FullThinking.Length}",
            LlmToolUseStartedEvent toolStart => $"tool_use_start id={toolStart.ToolCallId} name={toolStart.ToolName}",
            LlmToolUseArgumentsDeltaEvent args => $"tool_use_args id={args.ToolCallId} chars={args.PartialArgumentsJson.Length}",
            LlmToolUseCompletedEvent toolEnd => $"tool_use_end id={toolEnd.ToolCallId}",
            LlmCompletedEvent completed => $"completed text_chars={completed.FullText?.Length ?? 0} tool_calls={completed.ToolCalls.Count}",
            LlmErrorEvent error => $"error category={error.Category} message={error.Message}",
            _ => evt.GetType().Name
        };
    }

    private static string FormatRequestHeaders(HttpRequestMessage request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            if (IsSensitiveHeader(header.Key))
                continue;

            headers[header.Key] = string.Join(",", header.Value);
        }

        return JsonSerializer.Serialize(headers, JsonDefaults.Options);
    }

    private static string FormatResponseHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            if (IsSensitiveHeader(header.Key))
                continue;

            headers[header.Key] = string.Join(",", header.Value);
        }

        foreach (var header in response.Content.Headers)
        {
            if (IsSensitiveHeader(header.Key))
                continue;

            headers[header.Key] = string.Join(",", header.Value);
        }

        return JsonSerializer.Serialize(headers, JsonDefaults.Options);
    }

    private static bool IsSensitiveHeader(string headerName)
    {
        var name = headerName.Trim().ToLowerInvariant();
        return name is "authorization" or "x-api-key" or "cookie" or "set-cookie";
    }

    private static string Truncate(string text, int maxChars = 8192)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        return text[..maxChars] + "...(truncated)";
    }

    private IEnumerable<LlmStreamEvent> ProcessChunk(string data, StreamState state)
    {
        OpenAiStreamChunk? chunk;
        try
        {
            chunk = JsonSerializer.Deserialize<OpenAiStreamChunk>(data, JsonDefaults.Options);
        }
        catch (JsonException)
        {
            yield break;
        }

        if (chunk?.Usage != null)
            state.Usage = ToUsage(chunk.Usage);

        var choice = chunk?.Choices?.FirstOrDefault();
        if (choice == null)
            yield break;

        var delta = choice.Delta;
        if (!string.IsNullOrEmpty(delta?.Content))
        {
            state.TextBuilder.Append(delta.Content);
            yield return new LlmTextDeltaEvent(delta.Content);
        }

        if (delta?.ToolCalls != null)
        {
            foreach (var toolDelta in delta.ToolCalls)
            {
                if (!state.ToolCalls.TryGetValue(toolDelta.Index, out var toolState))
                {
                    var initialId = ToolCallIdNormalizer.Normalize(toolDelta.Id, toolDelta.Index);
                    var initialName = toolDelta.Function?.Name ?? string.Empty;

                    toolState = new MutableToolCall(toolDelta.Index, initialId)
                    {
                        Name = initialName
                    };
                    state.ToolCalls[toolDelta.Index] = toolState;
                    yield return new LlmToolUseStartedEvent(toolState.Id, toolState.Name);
                }

                if (!string.IsNullOrWhiteSpace(toolDelta.Function?.Name))
                    toolState.Name = toolDelta.Function.Name;

                if (!string.IsNullOrEmpty(toolDelta.Function?.Arguments))
                {
                    toolState.ArgumentsBuilder.Append(toolDelta.Function.Arguments);
                    yield return new LlmToolUseArgumentsDeltaEvent(toolState.Id, toolDelta.Function.Arguments);
                }
            }
        }

        if (choice.FinishReason == "tool_calls")
        {
            foreach (var toolState in state.ToolCalls.Values.OrderBy(x => x.Index))
            {
                if (state.CompletedToolIds.Add(toolState.Id))
                    yield return new LlmToolUseCompletedEvent(toolState.Id);
            }
        }
    }

    private static Usage? ToUsage(OpenAiUsage usage)
    {
        if (usage.PromptTokens <= 0 && usage.CompletionTokens <= 0 && usage.CachedPromptTokens <= 0)
            return null;

        return new Usage(
            InputTokens: usage.PromptTokens,
            OutputTokens: usage.CompletionTokens,
            CacheReadTokens: usage.CachedPromptTokens,
            CacheWriteTokens: 0,
            Cost: new CostBreakdown(0, 0, 0, 0, 0));
    }

    private static OpenAiMessage ToOpenAiMessage(LlmMessage message)
    {
        return message.Role switch
        {
            LlmMessageRole.System => new OpenAiMessage
            {
                Role = "system",
                Content = MessageContent.FlattenText(message.Content)
            },
            LlmMessageRole.User => new OpenAiMessage
            {
                Role = "user",
                Content = MessageContent.FlattenText(message.Content)
            },
            LlmMessageRole.Tool => BuildToolResultMessage(message),
            LlmMessageRole.Assistant => BuildAssistantMessage(message),
            _ => throw new ArgumentOutOfRangeException(nameof(message.Role), message.Role, "Unsupported message role")
        };
    }

    private static OpenAiMessage BuildToolResultMessage(LlmMessage message)
    {
        var result = message.Content.OfType<ToolResultContentBlock>().FirstOrDefault();
        if (result == null)
            throw new InvalidOperationException("Tool message must contain ToolResultContentBlock");

        return new OpenAiMessage
        {
            Role = "tool",
            ToolCallId = result.ToolCallId,
            Content = result.ContentText
        };
    }

    private static OpenAiMessage BuildAssistantMessage(LlmMessage message)
    {
        var text = message.Content.OfType<TextContentBlock>().Select(x => x.Text).FirstOrDefault();
        var toolCalls = message.Content
            .OfType<ToolCallContentBlock>()
            .Select(call => new OpenAiToolCall
            {
                Id = call.ToolCallId,
                Type = "function",
                Function = new OpenAiFunctionCall
                {
                    Name = call.ToolName,
                    Arguments = call.ArgumentsJson
                }
            })
            .ToList();

        return new OpenAiMessage
        {
            Role = "assistant",
            Content = text,
            ToolCalls = toolCalls.Count == 0 ? null : toolCalls
        };
    }

    private static OpenAiTool ToOpenAiTool(ToolDefinition tool)
        => new()
        {
            Type = "function",
            Function = new OpenAiFunction
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = tool.ParametersSchema
            }
        };

    private sealed class StreamState
    {
        public StringBuilder TextBuilder { get; } = new();
        public Dictionary<int, MutableToolCall> ToolCalls { get; } = new();
        public HashSet<string> CompletedToolIds { get; } = new(StringComparer.Ordinal);
        public Usage? Usage { get; set; }
    }

    private sealed class MutableToolCall
    {
        public MutableToolCall(int index, string id)
        {
            Index = index;
            Id = id;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; set; } = string.Empty;
        public StringBuilder ArgumentsBuilder { get; } = new();
    }

    private sealed class OpenAiRequest
    {
        public required string Model { get; init; }
        public required List<OpenAiMessage> Messages { get; init; }
        public List<OpenAiTool>? Tools { get; init; }
        public bool Stream { get; init; }
        public OpenAiStreamOptions? StreamOptions { get; init; }
        public int? MaxTokens { get; init; }
    }

    private sealed class OpenAiStreamOptions
    {
        public bool IncludeUsage { get; init; }
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
        public required JsonElement Parameters { get; init; }
    }

    private sealed class OpenAiToolCall
    {
        public required string Id { get; init; }
        public required string Type { get; init; }
        public required OpenAiFunctionCall Function { get; init; }
    }

    private sealed class OpenAiFunctionCall
    {
        public required string Name { get; init; }
        public required string Arguments { get; init; }
    }

    private sealed class OpenAiStreamChunk
    {
        public List<OpenAiStreamChoice>? Choices { get; init; }
        public OpenAiUsage? Usage { get; init; }
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

    private sealed class OpenAiUsage
    {
        public int PromptTokens { get; init; }
        public int CompletionTokens { get; init; }
        public OpenAiPromptTokensDetails? PromptTokensDetails { get; init; }

        public int CachedPromptTokens => PromptTokensDetails?.CachedTokens ?? 0;
    }

    private sealed class OpenAiPromptTokensDetails
    {
        public int CachedTokens { get; init; }
    }
}
