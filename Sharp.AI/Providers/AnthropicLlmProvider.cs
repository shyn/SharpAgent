using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sharp.AI.Providers;

public sealed class AnthropicLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AnthropicLlmProvider> _logger;

    public AnthropicLlmProvider(HttpClient httpClient, ILogger<AnthropicLlmProvider>? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger ?? NullLogger<AnthropicLlmProvider>.Instance;
    }

    public string ProviderId => "anthropic";

    public void Dispose() => _httpClient.Dispose();

    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        void Debug(string message) => request.OnDebugLog?.Invoke(message);

        if (request.Model.ApiKind != ProviderApiKind.AnthropicMessages)
        {
            yield return new LlmErrorEvent(
                $"Model '{request.Model.ModelId}' is not configured for Anthropic Messages API",
                LlmErrorCategory.Validation,
                Retryable: false);
            yield break;
        }

        var thinkingBudget = ResolveThinkingBudget(request.ThinkingLevel, request.ThinkingBudgets);
        var normalizedMessages = MessageTransforms.DropIncompleteAssistantTurns(request.Messages);
        normalizedMessages = MessageTransforms.EnsureToolResultContinuity(normalizedMessages);
        normalizedMessages = MessageTransforms.NormalizeToolCallIds(normalizedMessages, ToolCallIdNormalizer.Normalize);
        normalizedMessages = MessageTransforms.ConvertUnsignedThinkingToText(normalizedMessages);
        normalizedMessages = MessageTransforms.ConvertNonAnthropicThinkingSignaturesToText(normalizedMessages);
        var anthropicMessages = new List<AnthropicMessage>(normalizedMessages.Count);
        foreach (var m in normalizedMessages)
        {
            if (m.Role != LlmMessageRole.System)
            {
                anthropicMessages.Add(ToAnthropicMessage(m));
            }
        }

        List<AnthropicTool>? anthropicTools = null;
        if (request.Tools.Count > 0)
        {
            anthropicTools = new List<AnthropicTool>(request.Tools.Count);
            foreach (var tool in request.Tools)
            {
                anthropicTools.Add(ToAnthropicTool(tool));
            }
        }

        var payload = new AnthropicRequest
        {
            Model = request.Model.ModelId,
            MaxTokens = request.MaxOutputTokens ?? request.Model.MaxOutputTokens ?? 8192,
            System = request.SystemPrompt,
            Messages = anthropicMessages,
            Tools = anthropicTools,
            Stream = true,
            Thinking = thinkingBudget == null
                ? null
                : new AnthropicThinking { Type = "enabled", BudgetTokens = thinkingBudget.Value }
        };

        var payloadElement = JsonSerializer.SerializeToElement(payload, JsonDefaults.Options);
        request.OnPayload?.Invoke(payloadElement);

        var payloadJson = payloadElement.GetRawText();
        HttpRequestMessage CreateHttpRequest()
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "messages")
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            };

            if (payload.Thinking != null)
                httpRequest.Headers.TryAddWithoutValidation("anthropic-beta", "interleaved-thinking-2025-05-14");

            if (!string.IsNullOrWhiteSpace(request.SessionId))
                httpRequest.Headers.TryAddWithoutValidation("x-session-id", request.SessionId);

            ApplyHeaders(httpRequest, request.Headers);
            return httpRequest;
        }

        using var debugRequest = CreateHttpRequest();
        var requestUrl = ResolveRequestUri(debugRequest, "messages");
        Debug($"request.url={requestUrl}");
        Debug($"request.messages={request.Messages.Count} tools={request.Tools.Count}");
        Debug($"request.headers={FormatRequestHeaders(debugRequest)}");
        Debug($"request.payload={Truncate(payloadJson)}");

        const int maxAttempts = 3;
        HttpResponseMessage? response = null;
        LlmErrorEvent? terminalError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var httpRequest = CreateHttpRequest();
            try
            {
                response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                terminalError = new LlmErrorEvent("Anthropic request was aborted", LlmErrorCategory.Aborted, Retryable: true);
                break;
            }
            catch (TaskCanceledException)
            {
                if (attempt < maxAttempts)
                {
                    var delayMs = ComputeRetryDelayMs(attempt, null, request.MaxRetryDelayMs);
                    Debug($"request.retry attempt={attempt + 1} reason=timeout delay_ms={delayMs}");
                    await Task.Delay(delayMs, ct);
                    continue;
                }

                terminalError = new LlmErrorEvent("Anthropic request timed out", LlmErrorCategory.Timeout, Retryable: true);
                break;
            }
            catch (HttpRequestException ex)
            {
                if (attempt < maxAttempts)
                {
                    var delayMs = ComputeRetryDelayMs(attempt, null, request.MaxRetryDelayMs);
                    Debug($"request.retry attempt={attempt + 1} reason=network delay_ms={delayMs}");
                    await Task.Delay(delayMs, ct);
                    continue;
                }

                terminalError = new LlmErrorEvent($"Anthropic network error: {ex.Message}", LlmErrorCategory.Network, Retryable: true);
                break;
            }

            var safeAttemptResponse = response!;
            Debug($"response.status={(int)safeAttemptResponse.StatusCode}");
            Debug($"response.headers={FormatResponseHeaders(safeAttemptResponse)}");

            if (safeAttemptResponse.IsSuccessStatusCode)
                break;

            var body = await safeAttemptResponse.Content.ReadAsStringAsync(ct);
            var statusCode = (int)safeAttemptResponse.StatusCode;
            var category = ClassifyStatusCode(statusCode);
            var retryable = IsRetryableStatusCode(statusCode);
            Debug($"response.body={Truncate(body)}");

            if (LlmErrorSemantics.TryCreateContextOverflowError("Anthropic", statusCode, body, out var overflowError))
            {
                terminalError = overflowError;
                safeAttemptResponse.Dispose();
                response = null;
                break;
            }

            var hasRetryAfter = TryGetRetryAfterSeconds(safeAttemptResponse, out var retryAfterSeconds);
            if (request.MaxRetryDelayMs is > 0
                && hasRetryAfter
                && retryAfterSeconds * 1000 > request.MaxRetryDelayMs)
            {
                terminalError = new LlmErrorEvent(
                    $"Anthropic requested retry-after {retryAfterSeconds}s, above cap {request.MaxRetryDelayMs}ms",
                    LlmErrorCategory.RateLimit,
                    statusCode,
                    Retryable: true);
                safeAttemptResponse.Dispose();
                response = null;
                break;
            }

            if (retryable && attempt < maxAttempts)
            {
                var delayMs = ComputeRetryDelayMs(attempt, hasRetryAfter ? retryAfterSeconds : null, request.MaxRetryDelayMs);
                Debug($"request.retry attempt={attempt + 1} reason=http_{statusCode} delay_ms={delayMs}");
                safeAttemptResponse.Dispose();
                response = null;
                await Task.Delay(delayMs, ct);
                continue;
            }

            _logger.LogError("Anthropic stream request failed: HTTP {Status} {Body}", statusCode, body);
            terminalError = new LlmErrorEvent(
                $"Anthropic request failed with HTTP {statusCode}: {body}",
                category,
                statusCode,
                retryable);
            safeAttemptResponse.Dispose();
            response = null;
            break;
        }

        if (terminalError != null)
        {
            Debug($"transport.error={terminalError.Message} category={terminalError.Category}");
            yield return terminalError;
            yield break;
        }

        var safeResponse = response!;
        using (safeResponse)
        {
            var state = new StreamState(request.Model.Pricing);
            var parseableEventCount = 0;

            await foreach (var evt in ReadSseAsync(safeResponse, ct))
            {
                Debug($"response.sse_event={evt.Type} data={Truncate(evt.Data, 4096)}");
                foreach (var streamEvent in ProcessEvent(evt.Type, evt.Data, state))
                {
                    Debug($"response.event={DescribeStreamEvent(streamEvent)}");
                    parseableEventCount++;
                    yield return streamEvent;
                }
            }

            if (parseableEventCount == 0)
            {
                yield return new LlmErrorEvent(
                    "Anthropic stream produced no parseable events; provider may be incompatible with Anthropic Messages SSE format.",
                    LlmErrorCategory.Validation,
                    Retryable: false);
                yield break;
            }

            foreach (var toolState in state.ToolCalls.Values.OrderBy(x => x.Index))
            {
                if (state.CompletedToolIds.Add(toolState.Id))
                    yield return new LlmToolUseCompletedEvent(toolState.Id);
            }

            yield return new LlmCompletedEvent(
                state.TextBuilder.Length == 0 ? null : state.TextBuilder.ToString(),
                state.ThinkingBuilder.Length == 0 ? null : state.ThinkingBuilder.ToString(),
                state.ToolCalls.Values
                    .OrderBy(x => x.Index)
                    .Select(x => new ToolCall(x.Id, x.Name, x.ArgumentsBuilder.ToString(), x.Signature))
                    .ToList(),
                state.ToUsage(),
                state.ThinkingSignatureBuilder.Length == 0 ? null : state.ThinkingSignatureBuilder.ToString(),
                state.StopReason);
            Debug(
                $"response.completed text_chars={state.TextBuilder.Length} thinking_chars={state.ThinkingBuilder.Length} tool_calls={state.ToolCalls.Count}");
        }
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers == null)
            return;

        foreach (var header in headers)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
    }

    private Uri ResolveRequestUri(HttpRequestMessage request, string relativePath)
    {
        var baseAddress = _httpClient.BaseAddress ?? new Uri("http://localhost/");
        if (request.RequestUri == null)
            return new Uri(baseAddress, relativePath);

        return request.RequestUri.IsAbsoluteUri
            ? request.RequestUri
            : new Uri(baseAddress, request.RequestUri);
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

    private static int ComputeRetryDelayMs(int attempt, int? retryAfterSeconds, int? maxRetryDelayMs)
    {
        var exponentialMs = Math.Min(8000, 500 * (1 << (attempt - 1)));
        var jitterMs = Random.Shared.Next(0, 250);
        var delayMs = exponentialMs + jitterMs;

        if (retryAfterSeconds is > 0)
            delayMs = Math.Max(delayMs, retryAfterSeconds.Value * 1000);

        if (maxRetryDelayMs is > 0)
            delayMs = Math.Min(delayMs, maxRetryDelayMs.Value);

        return Math.Max(0, delayMs);
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

    private static int? ResolveThinkingBudget(ThinkingLevel level, ThinkingBudgets? budgets)
    {
        if (level == ThinkingLevel.Off)
            return null;

        var overrideBudget = budgets?.Resolve(level);
        if (overrideBudget is > 0)
            return overrideBudget;

        return level switch
        {
            ThinkingLevel.Minimal => 1024,
            ThinkingLevel.Low => 4096,
            ThinkingLevel.Medium => 16384,
            ThinkingLevel.High => 32768,
            ThinkingLevel.XHigh => 65536,
            _ => null
        };
    }

    private async IAsyncEnumerable<SseEvent> ReadSseAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? eventType = null;

        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null)
                yield break;

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventType = line[7..];
                continue;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal) && eventType != null)
            {
                yield return new SseEvent(eventType, line[6..]);
                continue;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var data = line[6..];
                if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
                    yield break;

                var resolvedType = ResolveEventTypeFromData(data);
                if (!string.IsNullOrEmpty(resolvedType))
                {
                    yield return new SseEvent(resolvedType, data);
                    continue;
                }
            }

            if (string.IsNullOrEmpty(line))
                eventType = null;
        }
    }

    private static string? ResolveEventTypeFromData(string data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            return document.RootElement.TryGetProperty("type", out var typeElement)
                   && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private IEnumerable<LlmStreamEvent> ProcessEvent(string eventType, string data, StreamState state)
    {
        switch (eventType)
        {
            case "message_start":
                {
                    var start = JsonSerializer.Deserialize<MessageStartPayload>(data, JsonDefaults.Options);
                    state.UpdateUsage(start?.Message?.Usage);
                    yield break;
                }
            case "message_delta":
                {
                    var delta = JsonSerializer.Deserialize<MessageDeltaPayload>(data, JsonDefaults.Options);
                    state.UpdateUsage(delta?.Usage);
                    if (!string.IsNullOrWhiteSpace(delta?.Delta?.StopReason))
                        state.StopReason = MapStopReason(delta.Delta.StopReason!);
                    yield break;
                }
            case "content_block_start":
                {
                    var start = JsonSerializer.Deserialize<ContentBlockStart>(data, JsonDefaults.Options);
                    var block = start?.ContentBlock;
                    if (block == null)
                        yield break;

                    state.CurrentContentType = block.Type;

                    if (block.Type == "thinking")
                        yield return new LlmThinkingStartedEvent();

                    if (block.Type == "tool_use")
                    {
                        var toolState = new MutableToolCall(
                            start!.Index,
                            ToolCallIdNormalizer.Normalize(block.Id, start.Index))
                        {
                            Name = block.Name ?? string.Empty,
                            Signature = block.Signature
                        };

                        state.ToolCalls[start.Index] = toolState;
                        yield return new LlmToolUseStartedEvent(toolState.Id, toolState.Name);
                    }

                    yield break;
                }
            case "content_block_delta":
                {
                    var delta = JsonSerializer.Deserialize<ContentBlockDelta>(data, JsonDefaults.Options);
                    var payload = delta?.Delta;
                    if (payload == null)
                        yield break;

                    if (payload.Type == "text_delta" && !string.IsNullOrEmpty(payload.Text))
                    {
                        state.TextBuilder.Append(payload.Text);
                        yield return new LlmTextDeltaEvent(payload.Text);
                    }

                    if (payload.Type == "thinking_delta" && !string.IsNullOrEmpty(payload.Thinking))
                    {
                        state.ThinkingBuilder.Append(payload.Thinking);
                        yield return new LlmThinkingDeltaEvent(payload.Thinking);
                    }

                    if (payload.Type == "signature_delta"
                        && !string.IsNullOrEmpty(payload.Signature)
                        && state.CurrentContentType == "thinking")
                    {
                        state.ThinkingSignatureBuilder.Append(payload.Signature);
                    }

                    if (payload.Type == "input_json_delta"
                        && !string.IsNullOrEmpty(payload.PartialJson)
                        && state.ToolCalls.TryGetValue(delta!.Index, out var toolState))
                    {
                        toolState.ArgumentsBuilder.Append(payload.PartialJson);
                        yield return new LlmToolUseArgumentsDeltaEvent(toolState.Id, payload.PartialJson);
                    }

                    yield break;
                }
            case "content_block_stop":
                {
                    if (state.CurrentContentType == "thinking")
                    {
                        var signature = state.ThinkingSignatureBuilder.Length == 0
                            ? null
                            : state.ThinkingSignatureBuilder.ToString();
                        yield return new LlmThinkingCompletedEvent(state.ThinkingBuilder.ToString(), signature);
                    }

                    if (state.CurrentContentType == "tool_use")
                    {
                        var indexPayload = JsonSerializer.Deserialize<ContentBlockStop>(data, JsonDefaults.Options);
                        if (indexPayload != null
                            && state.ToolCalls.TryGetValue(indexPayload.Index, out var tool)
                            && state.CompletedToolIds.Add(tool.Id))
                        {
                            yield return new LlmToolUseCompletedEvent(tool.Id);
                        }
                    }

                    state.CurrentContentType = null;
                    yield break;
                }
        }
    }

    private static AnthropicMessage ToAnthropicMessage(LlmMessage message)
    {
        if (message.Role == LlmMessageRole.Tool)
        {
            var toolResult = message.Content.OfType<ToolResultContentBlock>().FirstOrDefault()
                ?? throw new InvalidOperationException("Tool message must contain ToolResultContentBlock");

            return new AnthropicMessage
            {
                Role = "user",
                Content =
                [
                    new
                    {
                        type = "tool_result",
                        tool_use_id = toolResult.ToolCallId,
                        is_error = toolResult.IsError,
                        content = toolResult.ContentText
                    }
                ]
            };
        }

        if (message.Role == LlmMessageRole.Assistant)
        {
            var content = new List<object>();

            foreach (var block in message.Content)
            {
                switch (block)
                {
                    case ThinkingContentBlock thinking:
                        content.Add(new { type = "thinking", thinking = thinking.Text, signature = thinking.Signature });
                        break;
                    case TextContentBlock text:
                        content.Add(new { type = "text", text = text.Text });
                        break;
                    case ToolCallContentBlock toolCall:
                        content.Add(new
                        {
                            type = "tool_use",
                            id = toolCall.ToolCallId,
                            name = toolCall.ToolName,
                            input = ParseJsonObject(toolCall.ArgumentsJson),
                            signature = toolCall.Signature
                        });
                        break;
                }
            }

            return new AnthropicMessage
            {
                Role = "assistant",
                Content = content.ToArray()
            };
        }

        return new AnthropicMessage
        {
            Role = message.Role == LlmMessageRole.User ? "user" : "assistant",
            Content =
            [
                new { type = "text", text = MessageContent.FlattenText(message.Content) }
            ]
        };
    }

    private static JsonElement ParseJsonObject(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(value, JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new { });
        }
    }

    private static AnthropicTool ToAnthropicTool(ToolDefinition tool)
        => new()
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = tool.ParametersSchema
        };

    private static LlmStopReason MapStopReason(string stopReason)
    {
        return stopReason switch
        {
            "end_turn" => LlmStopReason.Stop,
            "max_tokens" => LlmStopReason.Length,
            "tool_use" => LlmStopReason.ToolUse,
            _ => LlmStopReason.Error
        };
    }

    private sealed class StreamState
    {
        public StreamState(ModelPricing? pricing)
        {
            Pricing = pricing;
        }

        public StringBuilder TextBuilder { get; } = new();
        public StringBuilder ThinkingBuilder { get; } = new();
        public StringBuilder ThinkingSignatureBuilder { get; } = new();
        public Dictionary<int, MutableToolCall> ToolCalls { get; } = new();
        public HashSet<string> CompletedToolIds { get; } = new(StringComparer.Ordinal);
        public string? CurrentContentType { get; set; }
        public LlmStopReason StopReason { get; set; } = LlmStopReason.Stop;
        public ModelPricing? Pricing { get; }

        public int InputTokens { get; private set; }
        public int OutputTokens { get; private set; }
        public int CacheReadTokens { get; private set; }
        public int CacheWriteTokens { get; private set; }

        public void UpdateUsage(AnthropicUsage? usage)
        {
            if (usage == null)
                return;

            InputTokens = Math.Max(InputTokens, usage.InputTokens);
            OutputTokens = Math.Max(OutputTokens, usage.OutputTokens);
            CacheReadTokens = Math.Max(CacheReadTokens, usage.CacheReadInputTokens);
            CacheWriteTokens = Math.Max(CacheWriteTokens, usage.CacheCreationInputTokens);
        }

        public Usage? ToUsage()
        {
            if (InputTokens <= 0 && OutputTokens <= 0 && CacheReadTokens <= 0 && CacheWriteTokens <= 0)
                return null;

            var value = new Usage(
                InputTokens,
                OutputTokens,
                CacheReadTokens,
                CacheWriteTokens,
                new CostBreakdown(0, 0, 0, 0, 0));
            return UsageCostCalculator.AttachCost(value, Pricing);
        }
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
        public string? Signature { get; set; }
        public StringBuilder ArgumentsBuilder { get; } = new();
    }

    private sealed record SseEvent(string Type, string Data);

    private sealed class AnthropicRequest
    {
        public required string Model { get; init; }
        public int MaxTokens { get; init; }
        public string? System { get; init; }
        public required List<AnthropicMessage> Messages { get; init; }
        public List<AnthropicTool>? Tools { get; init; }
        public bool Stream { get; init; }
        public AnthropicThinking? Thinking { get; init; }
    }

    private sealed class AnthropicThinking
    {
        public required string Type { get; init; }
        public int BudgetTokens { get; init; }
    }

    private sealed class AnthropicMessage
    {
        public required string Role { get; init; }
        public required object[] Content { get; init; }
    }

    private sealed class AnthropicTool
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required JsonElement InputSchema { get; init; }
    }

    private sealed class MessageStartPayload
    {
        public MessagePayload? Message { get; init; }
    }

    private sealed class MessagePayload
    {
        public AnthropicUsage? Usage { get; init; }
    }

    private sealed class MessageDeltaPayload
    {
        public MessageDelta? Delta { get; init; }
        public AnthropicUsage? Usage { get; init; }
    }

    private sealed class MessageDelta
    {
        public string? StopReason { get; init; }
    }

    private sealed class AnthropicUsage
    {
        public int InputTokens { get; init; }
        public int OutputTokens { get; init; }
        public int CacheCreationInputTokens { get; init; }
        public int CacheReadInputTokens { get; init; }
    }

    private sealed class ContentBlockStart
    {
        public int Index { get; init; }
        public ContentBlockPayload? ContentBlock { get; init; }
    }

    private sealed class ContentBlockPayload
    {
        public string? Type { get; init; }
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? Signature { get; init; }
    }

    private sealed class ContentBlockDelta
    {
        public int Index { get; init; }
        public DeltaPayload? Delta { get; init; }
    }

    private sealed class DeltaPayload
    {
        public string? Type { get; init; }
        public string? Text { get; init; }
        public string? Thinking { get; init; }
        public string? PartialJson { get; init; }
        public string? Signature { get; init; }
    }

    private sealed class ContentBlockStop
    {
        public int Index { get; init; }
    }
}
