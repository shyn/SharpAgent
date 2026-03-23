using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sharp.AI.Providers;

public sealed class OpenAiLlmProvider : ILlmProvider
{
    private const string ThinkingFormatOpenAi = "openai";
    private const string ThinkingFormatZai = "zai";
    private const string ThinkingFormatQwen = "qwen";

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

        var compat = request.Model.OpenAiCompletionsCompat ?? new OpenAiCompletionsCompat();
        var baseUrl = _httpClient.BaseAddress?.ToString() ?? string.Empty;
        var payloadCompat = ResolvePayloadCompat(request.Model.ProviderId, baseUrl, compat);
        var normalizedMessages = MessageTransforms.DropIncompleteAssistantTurns(request.Messages);
        normalizedMessages = MessageTransforms.EnsureToolResultContinuity(normalizedMessages);
        if (compat.RequiresAssistantAfterToolResult)
            normalizedMessages = MessageTransforms.EnsureAssistantAfterToolResult(normalizedMessages);
        if (compat.RequiresMistralToolIds)
        {
            normalizedMessages = MessageTransforms.NormalizeToolCallIds(
                normalizedMessages,
                ToolCallIdNormalizer.NormalizeMistral);
        }
        else if (ShouldNormalizeOpenAiToolCallIds(request.Model.ProviderId, baseUrl))
        {
            normalizedMessages = MessageTransforms.NormalizeToolCallIds(
                normalizedMessages,
                ToolCallIdNormalizer.NormalizeOpenAi);
        }

        var useDeveloperRole = request.ThinkingLevel != ThinkingLevel.Off && payloadCompat.SupportsDeveloperRole;
        var reasoningEffort = ResolveReasoningEffort(request.ThinkingLevel);
        var messages = new List<OpenAiMessage>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new OpenAiMessage
            {
                Role = useDeveloperRole ? "developer" : "system",
                Content = request.SystemPrompt
            });
        }

        messages.Capacity = messages.Count + normalizedMessages.Count;
        foreach (var message in normalizedMessages)
        {
            messages.Add(ToOpenAiMessage(message, compat, useDeveloperRole));
        }
        var openRouterRouting = IsOpenRouterBaseUrl(baseUrl)
            ? NormalizeRouting(payloadCompat.OpenRouterRouting)
            : null;
        var vercelRouting = IsVercelGatewayBaseUrl(baseUrl)
            ? NormalizeRouting(payloadCompat.VercelGatewayRouting)
            : null;

        List<OpenAiTool>? openAiTools = null;
        if (request.Tools.Count > 0)
        {
            openAiTools = new List<OpenAiTool>(request.Tools.Count);
            foreach (var tool in request.Tools)
            {
                openAiTools.Add(ToOpenAiTool(tool, compat));
            }
        }

        var payload = new OpenAiRequest
        {
            Model = request.Model.ModelId,
            Messages = messages,
            Tools = openAiTools,
            Stream = true,
            StreamOptions = compat.SupportsUsageInStreaming ? new OpenAiStreamOptions { IncludeUsage = true } : null,
            Store = payloadCompat.SupportsStore ? false : null,
            MaxTokens = compat.MaxTokensField == OpenAiMaxTokensField.MaxTokens
                ? request.MaxOutputTokens ?? request.Model.MaxOutputTokens
                : null,
            MaxCompletionTokens = compat.MaxTokensField == OpenAiMaxTokensField.MaxCompletionTokens
                ? request.MaxOutputTokens ?? request.Model.MaxOutputTokens
                : null,
            ReasoningEffort = payloadCompat.ThinkingFormat == ThinkingFormatOpenAi && payloadCompat.SupportsReasoningEffort
                ? reasoningEffort
                : null,
            Thinking = payloadCompat.ThinkingFormat == ThinkingFormatZai
                ? new OpenAiThinking { Type = reasoningEffort == null ? "disabled" : "enabled" }
                : null,
            EnableThinking = payloadCompat.ThinkingFormat == ThinkingFormatQwen
                ? reasoningEffort != null
                : null,
            Provider = openRouterRouting,
            ProviderOptions = vercelRouting == null
                ? null
                : new OpenAiProviderOptions { Gateway = vercelRouting }
        };

        var payloadElement = JsonSerializer.SerializeToElement(payload, JsonDefaults.Options);
        request.OnPayload?.Invoke(payloadElement);

        var payloadJson = payloadElement.GetRawText();
        HttpRequestMessage CreateHttpRequest()
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(request.SessionId))
                httpRequest.Headers.TryAddWithoutValidation("x-session-id", request.SessionId);

            ApplyHeaders(httpRequest, request.Headers);
            return httpRequest;
        }

        using var debugRequest = CreateHttpRequest();
        var requestUrl = ResolveRequestUri(debugRequest, "chat/completions");
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

            _logger.LogError("OpenAI stream request failed: HTTP {Status} {Body}", statusCode, body);
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

            var state = new StreamState(compat.RequiresMistralToolIds, request.Model.Pricing);

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
                state.Usage,
                StopReason: state.StopReason);
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
            state.Usage = ToUsage(chunk.Usage, state.Pricing);

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
                    var initialId = state.RequiresMistralToolIds
                        ? ToolCallIdNormalizer.NormalizeMistral(toolDelta.Id, toolDelta.Index)
                        : ToolCallIdNormalizer.Normalize(toolDelta.Id, toolDelta.Index);
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

        if (!string.IsNullOrWhiteSpace(choice.FinishReason))
            state.StopReason = MapStopReason(choice.FinishReason);

        if (choice.FinishReason == "tool_calls")
        {
            foreach (var toolState in state.ToolCalls.Values.OrderBy(x => x.Index))
            {
                if (state.CompletedToolIds.Add(toolState.Id))
                    yield return new LlmToolUseCompletedEvent(toolState.Id);
            }
        }
    }

    private static LlmStopReason MapStopReason(string finishReason)
    {
        return finishReason switch
        {
            "stop" => LlmStopReason.Stop,
            "length" => LlmStopReason.Length,
            "tool_calls" => LlmStopReason.ToolUse,
            _ => LlmStopReason.Error
        };
    }

    private static Usage? ToUsage(OpenAiUsage usage, ModelPricing? pricing)
    {
        if (usage.PromptTokens <= 0 && usage.CompletionTokens <= 0 && usage.CachedPromptTokens <= 0)
            return null;

        var value = new Usage(
            InputTokens: usage.PromptTokens,
            OutputTokens: usage.CompletionTokens,
            CacheReadTokens: usage.CachedPromptTokens,
            CacheWriteTokens: 0,
            Cost: new CostBreakdown(0, 0, 0, 0, 0));
        return UsageCostCalculator.AttachCost(value, pricing);
    }

    private static OpenAiMessage ToOpenAiMessage(
        LlmMessage message,
        OpenAiCompletionsCompat compat,
        bool useDeveloperRoleForSystem)
    {
        return message.Role switch
        {
            LlmMessageRole.System => new OpenAiMessage
            {
                Role = useDeveloperRoleForSystem ? "developer" : "system",
                Content = MessageContent.FlattenText(message.Content)
            },
            LlmMessageRole.User => new OpenAiMessage
            {
                Role = "user",
                Content = MessageContent.FlattenText(message.Content)
            },
            LlmMessageRole.Tool => BuildToolResultMessage(message, compat),
            LlmMessageRole.Assistant => BuildAssistantMessage(message, compat),
            _ => throw new ArgumentOutOfRangeException(nameof(message.Role), message.Role, "Unsupported message role")
        };
    }

    private static OpenAiMessage BuildToolResultMessage(LlmMessage message, OpenAiCompletionsCompat compat)
    {
        ToolResultContentBlock? result = null;
        foreach (var block in message.Content)
        {
            if (block is ToolResultContentBlock toolResultBlock)
            {
                result = toolResultBlock;
                break;
            }
        }

        if (result == null)
            throw new InvalidOperationException("Tool message must contain ToolResultContentBlock");

        return new OpenAiMessage
        {
            Role = "tool",
            ToolCallId = result.ToolCallId,
            Name = compat.RequiresToolResultName ? result.ToolName : null,
            Content = result.ContentText
        };
    }

    private static OpenAiMessage BuildAssistantMessage(LlmMessage message, OpenAiCompletionsCompat compat)
    {
        var hasTextBlock = false;
        var textParts = new List<string>();
        var toolCalls = new List<OpenAiToolCall>();

        foreach (var block in message.Content)
        {
            switch (block)
            {
                case TextContentBlock textBlock:
                    hasTextBlock = true;
                    if (textBlock.Text.Length > 0)
                        textParts.Add(textBlock.Text);
                    break;
                case ThinkingContentBlock thinking when !string.IsNullOrWhiteSpace(thinking.Text):
                    textParts.Add(compat.RequiresThinkingAsText
                        ? $"<thinking>\n{thinking.Text}\n</thinking>"
                        : thinking.Text);
                    break;
                case ToolCallContentBlock toolCall:
                    toolCalls.Add(new OpenAiToolCall
                    {
                        Id = toolCall.ToolCallId,
                        Type = "function",
                        Function = new OpenAiFunctionCall
                        {
                            Name = toolCall.ToolName,
                            Arguments = toolCall.ArgumentsJson
                        }
                    });
                    break;
            }
        }

        var text = textParts.Count > 0
            ? string.Join("\n", textParts)
            : hasTextBlock ? string.Empty : null;

        return new OpenAiMessage
        {
            Role = "assistant",
            Content = text,
            ToolCalls = toolCalls.Count == 0 ? null : toolCalls
        };
    }

    private static OpenAiTool ToOpenAiTool(ToolDefinition tool, OpenAiCompletionsCompat compat)
        => new()
        {
            Type = "function",
            Function = new OpenAiFunction
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = tool.ParametersSchema,
                Strict = compat.SupportsStrictMode ? false : null
            }
        };

    private static PayloadCompat ResolvePayloadCompat(
        string providerId,
        string baseUrl,
        OpenAiCompletionsCompat compat)
    {
        var detected = DetectPayloadCompat(providerId, baseUrl);
        var thinkingFormat = NormalizeThinkingFormat(compat.ThinkingFormat) ?? detected.ThinkingFormat;

        return new PayloadCompat
        {
            SupportsStore = compat.SupportsStore ?? detected.SupportsStore,
            SupportsDeveloperRole = compat.SupportsDeveloperRole ?? detected.SupportsDeveloperRole,
            SupportsReasoningEffort = compat.SupportsReasoningEffort ?? detected.SupportsReasoningEffort,
            ThinkingFormat = thinkingFormat,
            OpenRouterRouting = NormalizeRouting(compat.OpenRouterRouting),
            VercelGatewayRouting = NormalizeRouting(compat.VercelGatewayRouting)
        };
    }

    private static PayloadCompat DetectPayloadCompat(string providerId, string baseUrl)
    {
        var normalizedProviderId = providerId.Trim().ToLowerInvariant();
        var isZai = normalizedProviderId == "zai" || ContainsIgnoreCase(baseUrl, "api.z.ai");
        var isQwen = normalizedProviderId == "qwen" || ContainsIgnoreCase(baseUrl, "dashscope.aliyuncs.com");
        var isGrok = normalizedProviderId == "xai" || ContainsIgnoreCase(baseUrl, "api.x.ai");
        var isMistral = normalizedProviderId == "mistral" || ContainsIgnoreCase(baseUrl, "mistral.ai");
        var isNonStandard = normalizedProviderId is "cerebras" or "xai" or "mistral" or "opencode"
                            || ContainsIgnoreCase(baseUrl, "cerebras.ai")
                            || ContainsIgnoreCase(baseUrl, "chutes.ai")
                            || ContainsIgnoreCase(baseUrl, "deepseek.com")
                            || ContainsIgnoreCase(baseUrl, "opencode.ai")
                            || isGrok
                            || isZai
                            || isMistral;

        return new PayloadCompat
        {
            SupportsStore = !isNonStandard,
            SupportsDeveloperRole = !isNonStandard,
            SupportsReasoningEffort = !isGrok && !isZai,
            ThinkingFormat = isZai
                ? ThinkingFormatZai
                : isQwen
                    ? ThinkingFormatQwen
                    : ThinkingFormatOpenAi
        };
    }

    private static string? ResolveReasoningEffort(ThinkingLevel level)
    {
        return level switch
        {
            ThinkingLevel.Off => null,
            ThinkingLevel.Minimal => "minimal",
            ThinkingLevel.Low => "low",
            ThinkingLevel.Medium => "medium",
            ThinkingLevel.High => "high",
            ThinkingLevel.XHigh => "high",
            _ => null
        };
    }

    private static string? NormalizeThinkingFormat(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        var normalized = rawValue.Trim().ToLowerInvariant();
        return normalized is ThinkingFormatOpenAi or ThinkingFormatZai or ThinkingFormatQwen
            ? normalized
            : null;
    }

    private static OpenAiRoutingPreferences? NormalizeRouting(OpenAiRoutingPreferences? routing)
    {
        if (routing == null)
            return null;

        var only = routing.Only?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var order = routing.Order?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        only = only is { Length: > 0 } ? only : null;
        order = order is { Length: > 0 } ? order : null;
        if (only == null && order == null)
            return null;

        return new OpenAiRoutingPreferences(only, order);
    }

    private static bool IsOpenRouterBaseUrl(string baseUrl)
        => ContainsIgnoreCase(baseUrl, "openrouter.ai");

    private static bool IsVercelGatewayBaseUrl(string baseUrl)
        => ContainsIgnoreCase(baseUrl, "ai-gateway.vercel.sh");

    private static bool ContainsIgnoreCase(string text, string token)
        => text.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static bool ShouldNormalizeOpenAiToolCallIds(string providerId, string baseUrl)
    {
        var normalizedProviderId = providerId.Trim().ToLowerInvariant();
        return normalizedProviderId is "openai" or "openai-codex" or "opencode"
               || ContainsIgnoreCase(baseUrl, "api.openai.com");
    }

    private sealed class StreamState
    {
        public StreamState(bool requiresMistralToolIds, ModelPricing? pricing)
        {
            RequiresMistralToolIds = requiresMistralToolIds;
            Pricing = pricing;
        }

        public StringBuilder TextBuilder { get; } = new();
        public Dictionary<int, MutableToolCall> ToolCalls { get; } = new();
        public HashSet<string> CompletedToolIds { get; } = new(StringComparer.Ordinal);
        public Usage? Usage { get; set; }
        public LlmStopReason StopReason { get; set; } = LlmStopReason.Stop;
        public bool RequiresMistralToolIds { get; }
        public ModelPricing? Pricing { get; }
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
        public bool? Store { get; init; }
        public int? MaxTokens { get; init; }
        public int? MaxCompletionTokens { get; init; }
        public string? ReasoningEffort { get; init; }
        public OpenAiThinking? Thinking { get; init; }
        public bool? EnableThinking { get; init; }
        public OpenAiRoutingPreferences? Provider { get; init; }
        public OpenAiProviderOptions? ProviderOptions { get; init; }
    }

    private sealed class OpenAiStreamOptions
    {
        public bool IncludeUsage { get; init; }
    }

    private sealed class OpenAiThinking
    {
        public required string Type { get; init; }
    }

    private sealed class OpenAiProviderOptions
    {
        public required OpenAiRoutingPreferences Gateway { get; init; }
    }

    private sealed class PayloadCompat
    {
        public required bool SupportsStore { get; init; }
        public required bool SupportsDeveloperRole { get; init; }
        public required bool SupportsReasoningEffort { get; init; }
        public required string ThinkingFormat { get; init; }
        public OpenAiRoutingPreferences? OpenRouterRouting { get; init; }
        public OpenAiRoutingPreferences? VercelGatewayRouting { get; init; }
    }

    private sealed class OpenAiMessage
    {
        public required string Role { get; init; }
        public string? Content { get; init; }
        public string? ToolCallId { get; init; }
        public string? Name { get; init; }
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
        public bool? Strict { get; init; }
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
