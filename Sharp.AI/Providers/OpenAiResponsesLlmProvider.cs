using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sharp.AI.Providers;

public sealed class OpenAiResponsesLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiResponsesLlmProvider> _logger;

    public OpenAiResponsesLlmProvider(HttpClient httpClient, ILogger<OpenAiResponsesLlmProvider>? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger ?? NullLogger<OpenAiResponsesLlmProvider>.Instance;
    }

    public string ProviderId => "openai";

    public void Dispose() => _httpClient.Dispose();

    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        void Debug(string message) => request.OnDebugLog?.Invoke(message);

        if (request.Model.ApiKind != ProviderApiKind.OpenAiResponses)
        {
            yield return new LlmErrorEvent(
                $"Model '{request.Model.ModelId}' is not configured for OpenAI Responses",
                LlmErrorCategory.Validation,
                Retryable: false);
            yield break;
        }

        var payload = BuildRequestPayload(request);
        var payloadElement = JsonSerializer.SerializeToElement(payload, JsonDefaults.Options);
        request.OnPayload?.Invoke(payloadElement);

        var payloadJson = payloadElement.GetRawText();
        HttpRequestMessage CreateHttpRequest()
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "responses")
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(request.SessionId))
                httpRequest.Headers.TryAddWithoutValidation("x-session-id", request.SessionId);

            ApplyHeaders(httpRequest, request.Headers);
            return httpRequest;
        }

        using var debugRequest = CreateHttpRequest();
        var requestUrl = ResolveRequestUri(debugRequest, "responses");
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
                terminalError = new LlmErrorEvent("OpenAI request was aborted", LlmErrorCategory.Aborted, Retryable: true);
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

                terminalError = new LlmErrorEvent("OpenAI request timed out", LlmErrorCategory.Timeout, Retryable: true);
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

                terminalError = new LlmErrorEvent($"OpenAI network error: {ex.Message}", LlmErrorCategory.Network, Retryable: true);
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

            if (LlmErrorSemantics.TryCreateContextOverflowError("OpenAI", statusCode, body, out var overflowError))
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
                    $"OpenAI requested retry-after {retryAfterSeconds}s, above cap {request.MaxRetryDelayMs}ms",
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

            _logger.LogError("OpenAI responses request failed: HTTP {Status} {Body}", statusCode, body);
            terminalError = new LlmErrorEvent(
                $"OpenAI request failed with HTTP {statusCode}: {body}",
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
            await using var stream = await safeResponse.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            var state = new StreamState(request.Model.Pricing);
            LlmErrorEvent? streamError = null;

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

                foreach (var evt in ProcessEvent(data, state))
                {
                    Debug($"response.event={DescribeStreamEvent(evt)}");
                    if (evt is LlmErrorEvent errorEvent)
                    {
                        streamError = errorEvent;
                        break;
                    }

                    yield return evt;
                }

                if (streamError != null)
                    break;
            }

            if (streamError != null)
            {
                yield return streamError;
                yield break;
            }

            foreach (var toolState in state.ToolCalls.Values.OrderBy(x => x.Index))
            {
                if (state.CompletedToolIds.Add(toolState.Id))
                    yield return new LlmToolUseCompletedEvent(toolState.Id);
            }

            var finalStopReason = state.StopReason;
            if (finalStopReason == LlmStopReason.Stop && state.ToolCalls.Count > 0)
                finalStopReason = LlmStopReason.ToolUse;

            var toolCalls = new List<ToolCall>(state.ToolCalls.Count);
            foreach (var toolState in state.ToolCalls.Values.OrderBy(x => x.Index))
            {
                toolCalls.Add(new ToolCall(toolState.Id, toolState.Name, toolState.ArgumentsBuilder.ToString()));
            }

            yield return new LlmCompletedEvent(
                state.TextBuilder.Length == 0 ? null : state.TextBuilder.ToString(),
                state.ThinkingBuilder.Length == 0 ? null : state.ThinkingBuilder.ToString(),
                toolCalls,
                state.Usage,
                state.ThinkingSignature,
                finalStopReason);
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
            LlmCompletedEvent completed => $"completed text_chars={completed.FullText?.Length ?? 0} tool_calls={completed.ToolCalls.Count} reason={completed.StopReason}",
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

    private static OpenAiResponsesRequest BuildRequestPayload(LlmRequest request)
    {
        var normalizedMessages = MessageTransforms.DropIncompleteAssistantTurns(request.Messages);
        normalizedMessages = MessageTransforms.EnsureToolResultContinuity(normalizedMessages);
        var input = BuildInput(request.SystemPrompt, normalizedMessages);

        List<OpenAiResponsesTool>? tools = null;
        if (request.Tools.Count > 0)
        {
            tools = new List<OpenAiResponsesTool>(request.Tools.Count);
            foreach (var tool in request.Tools)
            {
                tools.Add(ToOpenAiTool(tool));
            }
        }

        return new OpenAiResponsesRequest
        {
            Model = request.Model.ModelId,
            Input = input,
            Tools = tools,
            Stream = true,
            MaxOutputTokens = request.MaxOutputTokens ?? request.Model.MaxOutputTokens
        };
    }

    private static List<object> BuildInput(string? systemPrompt, IReadOnlyList<LlmMessage> messages)
    {
        var input = new List<object>();
        var callIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var nextCallIndex = 0;

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            input.Add(new
            {
                role = "system",
                content = systemPrompt
            });
        }

        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case LlmMessageRole.System:
                    {
                        var text = MessageContent.FlattenText(message.Content);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            input.Add(new
                            {
                                role = "system",
                                content = text
                            });
                        }
                        break;
                    }
                case LlmMessageRole.User:
                    {
                        var hasImages = message.Content.OfType<ImageContentBlock>().Any();
                        if (!hasImages)
                        {
                            input.Add(new
                            {
                                role = "user",
                                content = MessageContent.FlattenText(message.Content)
                            });
                            break;
                        }

                        var content = new List<object>();
                        var textBlocks = new List<string>();
                        foreach (var block in message.Content)
                        {
                            if (block is TextContentBlock textBlock && !string.IsNullOrWhiteSpace(textBlock.Text))
                            {
                                textBlocks.Add(textBlock.Text);
                            }
                        }

                        var text = string.Join("\n", textBlocks);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            content.Add(new
                            {
                                type = "input_text",
                                text
                            });
                        }

                        foreach (var block in message.Content)
                        {
                            if (block is ImageContentBlock image)
                            {
                                content.Add(new
                                {
                                    type = "input_image",
                                    image_url = $"data:{image.MimeType};base64,{image.Base64Data}",
                                    detail = "auto"
                                });
                            }
                        }

                        input.Add(new
                        {
                            role = "user",
                            content = content.ToArray()
                        });
                        break;
                    }
                case LlmMessageRole.Assistant:
                    {
                        var textParts = new List<string>();
                        var reasoningItems = new List<JsonElement>();
                        foreach (var block in message.Content)
                        {
                            switch (block)
                            {
                                case TextContentBlock text:
                                    textParts.Add(text.Text);
                                    break;
                                case ThinkingContentBlock thinking:
                                    if (!string.IsNullOrWhiteSpace(thinking.Signature)
                                        && ThinkingSignatureInterop.TryNormalizeOpenAiReasoningItem(
                                            thinking.Signature,
                                            out var reasoningItem,
                                            out _))
                                    {
                                        reasoningItems.Add(reasoningItem);
                                        break;
                                    }

                                    if (!string.IsNullOrWhiteSpace(thinking.Text))
                                        textParts.Add($"<thinking>\n{thinking.Text}\n</thinking>");
                                    break;
                            }
                        }

                        var toolCalls = message.Content.OfType<ToolCallContentBlock>().ToList();
                        // Treat "signature-only" assistant turns as aborted/incomplete history and skip replay.
                        // These turns can cause OpenAI Responses to reject the request with orphaned reasoning items.
                        if (reasoningItems.Count > 0 && textParts.Count == 0 && toolCalls.Count == 0)
                            break;

                        foreach (var reasoningItem in reasoningItems)
                            input.Add(reasoningItem);

                        var assistantText = string.Join("\n", textParts.Where(x => !string.IsNullOrWhiteSpace(x)));
                        if (!string.IsNullOrWhiteSpace(assistantText))
                        {
                            input.Add(new
                            {
                                role = "assistant",
                                content = assistantText
                            });
                        }

                        foreach (var call in toolCalls)
                        {
                            var callId = ResolveCallId(call.ToolCallId, callIdMap, ref nextCallIndex);
                            input.Add(new
                            {
                                type = "function_call",
                                call_id = callId,
                                name = call.ToolName,
                                arguments = NormalizeJsonObject(call.ArgumentsJson)
                            });
                        }

                        break;
                    }
                case LlmMessageRole.Tool:
                    {
                        foreach (var toolResult in message.Content.OfType<ToolResultContentBlock>())
                        {
                            var callId = ResolveCallId(toolResult.ToolCallId, callIdMap, ref nextCallIndex);
                            input.Add(new
                            {
                                type = "function_call_output",
                                call_id = callId,
                                output = toolResult.ContentText
                            });
                        }
                        break;
                    }
            }
        }

        return input;
    }

    private static string ResolveCallId(
        string rawToolCallId,
        IDictionary<string, string> callIdMap,
        ref int nextCallIndex)
    {
        var raw = string.IsNullOrWhiteSpace(rawToolCallId)
            ? $"call_{nextCallIndex}"
            : rawToolCallId.Split('|', 2)[0];

        if (callIdMap.TryGetValue(raw, out var mapped))
            return mapped;

        var normalized = ToolCallIdNormalizer.Normalize(raw, nextCallIndex);
        callIdMap[raw] = normalized;
        nextCallIndex++;
        return normalized;
    }

    private static string NormalizeJsonObject(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return "{}";

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.GetRawText()
                : "{}";
        }
        catch (JsonException)
        {
            return "{}";
        }
    }

    private static OpenAiResponsesTool ToOpenAiTool(ToolDefinition tool)
        => new()
        {
            Type = "function",
            Name = tool.Name,
            Description = tool.Description,
            Parameters = tool.ParametersSchema
        };

    private IEnumerable<LlmStreamEvent> ProcessEvent(string data, StreamState state)
    {
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(data);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            var root = doc.RootElement;
            var type = TryGetString(root, "type");
            if (string.IsNullOrWhiteSpace(type))
                yield break;

            switch (type)
            {
                case "response.output_item.added":
                    {
                        if (!TryGetProperty(root, "item", out var item))
                            yield break;

                        var itemType = TryGetString(item, "type");
                        if (itemType == "reasoning" && !state.ThinkingStarted)
                        {
                            state.ThinkingStarted = true;
                            yield return new LlmThinkingStartedEvent();
                            yield break;
                        }

                        if (itemType == "function_call")
                        {
                            var rawCallId = TryGetString(item, "call_id") ?? TryGetString(item, "id");
                            var toolName = TryGetString(item, "name") ?? string.Empty;
                            var created = TryCreateOrGetToolState(state, rawCallId, toolName, out var toolState);
                            if (created)
                                yield return new LlmToolUseStartedEvent(toolState!.Id, toolState.Name);

                            var arguments = TryGetString(item, "arguments");
                            if (!string.IsNullOrWhiteSpace(arguments))
                            {
                                toolState!.ArgumentsBuilder.Append(arguments);
                                yield return new LlmToolUseArgumentsDeltaEvent(toolState.Id, arguments);
                            }
                        }

                        yield break;
                    }
                case "response.output_text.delta":
                    {
                        var delta = TryGetString(root, "delta");
                        if (!string.IsNullOrEmpty(delta))
                        {
                            state.TextBuilder.Append(delta);
                            yield return new LlmTextDeltaEvent(delta);
                        }

                        yield break;
                    }
                case "response.reasoning_summary_text.delta":
                case "response.reasoning_text.delta":
                    {
                        var delta = TryGetString(root, "delta");
                        if (!string.IsNullOrEmpty(delta))
                        {
                            if (!state.ThinkingStarted)
                            {
                                state.ThinkingStarted = true;
                                yield return new LlmThinkingStartedEvent();
                            }

                            state.ThinkingBuilder.Append(delta);
                            yield return new LlmThinkingDeltaEvent(delta);
                        }

                        yield break;
                    }
                case "response.function_call_arguments.delta":
                    {
                        var rawCallId = TryGetString(root, "call_id")
                                        ?? ResolveCallIdFromItemId(root, state)
                                        ?? $"call_{state.NextToolIndex}";
                        var created = TryCreateOrGetToolState(state, rawCallId, null, out var toolState);
                        if (created)
                            yield return new LlmToolUseStartedEvent(toolState!.Id, toolState.Name);

                        var delta = TryGetString(root, "delta");
                        if (!string.IsNullOrEmpty(delta))
                        {
                            toolState!.ArgumentsBuilder.Append(delta);
                            yield return new LlmToolUseArgumentsDeltaEvent(toolState.Id, delta);
                        }

                        yield break;
                    }
                case "response.function_call_arguments.done":
                    {
                        var rawCallId = TryGetString(root, "call_id")
                                        ?? ResolveCallIdFromItemId(root, state)
                                        ?? $"call_{state.NextToolIndex}";
                        var created = TryCreateOrGetToolState(state, rawCallId, null, out var toolState);
                        if (created)
                            yield return new LlmToolUseStartedEvent(toolState!.Id, toolState.Name);

                        var fullArguments = TryGetString(root, "arguments");
                        if (!string.IsNullOrWhiteSpace(fullArguments))
                        {
                            toolState!.ArgumentsBuilder.Clear();
                            toolState.ArgumentsBuilder.Append(fullArguments);
                        }

                        yield break;
                    }
                case "response.output_item.done":
                    {
                        if (!TryGetProperty(root, "item", out var item))
                            yield break;

                        var itemType = TryGetString(item, "type");
                        if (itemType == "reasoning")
                        {
                            state.ThinkingSignature = item.GetRawText();
                            if (state.ThinkingStarted)
                            {
                                yield return new LlmThinkingCompletedEvent(
                                    state.ThinkingBuilder.ToString(),
                                    state.ThinkingSignature);
                            }
                        }
                        else if (itemType == "function_call")
                        {
                            var rawCallId = TryGetString(item, "call_id") ?? TryGetString(item, "id");
                            var toolName = TryGetString(item, "name");
                            var created = TryCreateOrGetToolState(state, rawCallId, toolName, out var toolState);
                            if (created)
                                yield return new LlmToolUseStartedEvent(toolState!.Id, toolState.Name);

                            var fullArguments = TryGetString(item, "arguments");
                            if (!string.IsNullOrWhiteSpace(fullArguments))
                            {
                                toolState!.ArgumentsBuilder.Clear();
                                toolState.ArgumentsBuilder.Append(fullArguments);
                            }

                            if (toolState != null && state.CompletedToolIds.Add(toolState.Id))
                                yield return new LlmToolUseCompletedEvent(toolState.Id);
                        }

                        yield break;
                    }
                case "response.completed":
                    {
                        if (TryGetProperty(root, "response", out var response))
                        {
                            state.Usage = ParseUsage(response, state.Pricing);
                            state.StopReason = ParseStopReason(response, state);
                        }

                        yield break;
                    }
                case "response.failed":
                case "error":
                    {
                        var message = ExtractErrorMessage(root) ?? "OpenAI responses stream failed";
                        yield return new LlmErrorEvent(message, LlmErrorCategory.Validation, Retryable: false);
                        yield break;
                    }
            }
        }
    }

    private static bool TryCreateOrGetToolState(
        StreamState state,
        string? rawCallId,
        string? toolName,
        out MutableToolCall? toolState)
    {
        var rawId = string.IsNullOrWhiteSpace(rawCallId)
            ? $"call_{state.NextToolIndex}"
            : rawCallId.Split('|', 2)[0];

        if (state.ToolCalls.TryGetValue(rawId, out toolState))
        {
            if (!string.IsNullOrWhiteSpace(toolName))
                toolState.Name = toolName;
            return false;
        }

        var normalizedId = ToolCallIdNormalizer.Normalize(rawId, state.NextToolIndex);
        toolState = new MutableToolCall(state.NextToolIndex, rawId, normalizedId)
        {
            Name = toolName ?? string.Empty
        };
        state.ToolCalls[rawId] = toolState;
        state.NextToolIndex++;
        return true;
    }

    private static string? ResolveCallIdFromItemId(JsonElement root, StreamState state)
    {
        var itemId = TryGetString(root, "item_id");
        if (string.IsNullOrWhiteSpace(itemId))
            return null;

        var itemKey = itemId.Split('|', 2)[0];
        if (state.ToolCalls.TryGetValue(itemKey, out var existing))
            return existing.RawId;

        return itemKey;
    }

    private static Usage? ParseUsage(JsonElement response, ModelPricing? pricing)
    {
        if (!TryGetProperty(response, "usage", out var usage))
            return null;

        var inputTokens = TryGetInt32(usage, "input_tokens");
        var outputTokens = TryGetInt32(usage, "output_tokens");
        var cacheReadTokens = 0;

        if (TryGetProperty(usage, "input_tokens_details", out var inputDetails))
            cacheReadTokens = TryGetInt32(inputDetails, "cached_tokens");

        if (inputTokens <= 0 && outputTokens <= 0 && cacheReadTokens <= 0)
            return null;

        var value = new Usage(
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            CacheReadTokens: cacheReadTokens,
            CacheWriteTokens: 0,
            Cost: new CostBreakdown(0, 0, 0, 0, 0));
        return UsageCostCalculator.AttachCost(value, pricing);
    }

    private static LlmStopReason ParseStopReason(JsonElement response, StreamState state)
    {
        var status = TryGetString(response, "status");
        if (string.Equals(status, "incomplete", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetProperty(response, "incomplete_details", out var details))
            {
                var reason = TryGetString(details, "reason");
                if (string.Equals(reason, "max_output_tokens", StringComparison.OrdinalIgnoreCase))
                    return LlmStopReason.Length;
            }

            return LlmStopReason.Error;
        }

        if (state.ToolCalls.Count > 0)
            return LlmStopReason.ToolUse;

        return LlmStopReason.Stop;
    }

    private static string? ExtractErrorMessage(JsonElement root)
    {
        if (TryGetProperty(root, "error", out var directError))
            return TryGetString(directError, "message") ?? directError.GetRawText();

        if (TryGetProperty(root, "response", out var response)
            && TryGetProperty(response, "error", out var responseError))
        {
            return TryGetString(responseError, "message") ?? responseError.GetRawText();
        }

        return null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        return 0;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
        => element.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.Object;

    private sealed class StreamState
    {
        public StreamState(ModelPricing? pricing)
        {
            Pricing = pricing;
        }

        public StringBuilder TextBuilder { get; } = new();
        public StringBuilder ThinkingBuilder { get; } = new();
        public Dictionary<string, MutableToolCall> ToolCalls { get; } = new(StringComparer.Ordinal);
        public HashSet<string> CompletedToolIds { get; } = new(StringComparer.Ordinal);
        public Usage? Usage { get; set; }
        public ModelPricing? Pricing { get; }
        public string? ThinkingSignature { get; set; }
        public bool ThinkingStarted { get; set; }
        public int NextToolIndex { get; set; }
        public LlmStopReason StopReason { get; set; } = LlmStopReason.Stop;
    }

    private sealed class MutableToolCall
    {
        public MutableToolCall(int index, string rawId, string id)
        {
            Index = index;
            RawId = rawId;
            Id = id;
        }

        public int Index { get; }
        public string RawId { get; }
        public string Id { get; }
        public string Name { get; set; } = string.Empty;
        public StringBuilder ArgumentsBuilder { get; } = new();
    }

    private sealed class OpenAiResponsesRequest
    {
        public required string Model { get; init; }
        public required List<object> Input { get; init; }
        public List<OpenAiResponsesTool>? Tools { get; init; }
        public bool Stream { get; init; }
        public int? MaxOutputTokens { get; init; }
    }

    private sealed class OpenAiResponsesTool
    {
        public required string Type { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required JsonElement Parameters { get; init; }
    }
}
