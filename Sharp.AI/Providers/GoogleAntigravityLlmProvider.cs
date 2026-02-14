using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sharp.AI.Providers;

public sealed class GoogleAntigravityLlmProvider : ILlmProvider
{
    private const string DefaultVersion = "1.15.8";
    private const string ClaudeThinkingBetaHeader = "interleaved-thinking-2025-05-14";
    private const string AntigravitySystemInstruction =
        "You are Antigravity, a powerful agentic AI coding assistant designed by the Google Deepmind team working on Advanced Agentic Coding."
        + "You are pair programming with a USER to solve their coding task. The task may require creating a new codebase, modifying or debugging an existing codebase, or simply answering a question."
        + "**Absolute paths only**"
        + "**Proactiveness**";

    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleAntigravityLlmProvider> _logger;
    private readonly string _projectId;

    public GoogleAntigravityLlmProvider(
        HttpClient httpClient,
        string projectId,
        ILogger<GoogleAntigravityLlmProvider>? logger = null)
    {
        _httpClient = httpClient;
        _projectId = string.IsNullOrWhiteSpace(projectId) ? "rising-fact-p41fc" : projectId.Trim();
        _logger = logger ?? NullLogger<GoogleAntigravityLlmProvider>.Instance;
    }

    public string ProviderId => "google-antigravity";

    public void Dispose() => _httpClient.Dispose();

    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        void Debug(string message) => request.OnDebugLog?.Invoke(message);

        if (request.Model.ApiKind != ProviderApiKind.GoogleGeminiCli)
        {
            yield return new LlmErrorEvent(
                $"Model '{request.Model.ModelId}' is not configured for Google Antigravity",
                LlmErrorCategory.Validation,
                Retryable: false);
            yield break;
        }

        var normalizedMessages = MessageTransforms.DropIncompleteAssistantTurns(request.Messages);
        normalizedMessages = MessageTransforms.EnsureToolResultContinuity(normalizedMessages);

        var payload = BuildRequestPayload(request, normalizedMessages);
        var payloadElement = JsonSerializer.SerializeToElement(payload, RequestJsonOptions);
        request.OnPayload?.Invoke(payloadElement);
        var payloadJson = payloadElement.GetRawText();

        HttpRequestMessage CreateHttpRequest()
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1internal:streamGenerateContent?alt=sse")
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            };

            httpRequest.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
            httpRequest.Headers.TryAddWithoutValidation("User-Agent", BuildUserAgent());
            httpRequest.Headers.TryAddWithoutValidation("X-Goog-Api-Client", "google-cloud-sdk vscode_cloudshelleditor/0.1");
            httpRequest.Headers.TryAddWithoutValidation(
                "Client-Metadata",
                "{\"ideType\":\"IDE_UNSPECIFIED\",\"platform\":\"PLATFORM_UNSPECIFIED\",\"pluginType\":\"GEMINI\"}");

            if (IsClaudeThinkingModel(request.Model.ModelId))
                httpRequest.Headers.TryAddWithoutValidation("anthropic-beta", ClaudeThinkingBetaHeader);

            ApplyHeaders(httpRequest, request.Headers);
            return httpRequest;
        }

        using var debugRequest = CreateHttpRequest();
        var requestUrl = ResolveRequestUri(debugRequest, "v1internal:streamGenerateContent?alt=sse");
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
                terminalError = new LlmErrorEvent("Google Antigravity request was aborted", LlmErrorCategory.Aborted, Retryable: true);
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

                terminalError = new LlmErrorEvent("Google Antigravity request timed out", LlmErrorCategory.Timeout, Retryable: true);
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

                terminalError = new LlmErrorEvent($"Google Antigravity network error: {ex.Message}", LlmErrorCategory.Network, Retryable: true);
                break;
            }

            var safeAttemptResponse = response!;
            Debug($"response.status={(int)safeAttemptResponse.StatusCode}");
            Debug($"response.headers={FormatResponseHeaders(safeAttemptResponse)}");

            if (safeAttemptResponse.IsSuccessStatusCode)
                break;

            var body = await safeAttemptResponse.Content.ReadAsStringAsync(ct);
            var statusCode = (int)safeAttemptResponse.StatusCode;
            Debug($"response.body={Truncate(body)}");

            if (LlmErrorSemantics.TryCreateContextOverflowError("Google Antigravity", statusCode, body, out var overflowError))
            {
                terminalError = overflowError;
                safeAttemptResponse.Dispose();
                response = null;
                break;
            }

            var retryable = IsRetryableStatusCode(statusCode) || IsRetryableBody(body);
            var hasRetryDelay = TryGetRetryDelayMs(safeAttemptResponse, body, out var retryDelayMs);
            if (request.MaxRetryDelayMs is > 0 && hasRetryDelay && retryDelayMs > request.MaxRetryDelayMs)
            {
                terminalError = new LlmErrorEvent(
                    $"Google Antigravity requested retry delay {retryDelayMs}ms, above cap {request.MaxRetryDelayMs}ms",
                    LlmErrorCategory.RateLimit,
                    statusCode,
                    Retryable: true);
                safeAttemptResponse.Dispose();
                response = null;
                break;
            }

            if (retryable && attempt < maxAttempts)
            {
                var delayMs = ComputeRetryDelayMs(
                    attempt,
                    hasRetryDelay ? (int)Math.Ceiling(retryDelayMs / 1000d) : null,
                    request.MaxRetryDelayMs);
                Debug($"request.retry attempt={attempt + 1} reason=http_{statusCode} delay_ms={delayMs}");
                safeAttemptResponse.Dispose();
                response = null;
                await Task.Delay(delayMs, ct);
                continue;
            }

            _logger.LogError("Google Antigravity request failed: HTTP {Status} {Body}", statusCode, body);
            terminalError = new LlmErrorEvent(
                $"Google Antigravity request failed with HTTP {statusCode}: {ExtractErrorMessage(body)}",
                ClassifyStatusCode(statusCode),
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
            var parseableEventCount = 0;

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                Debug($"response.line={Truncate(line, 4096)}");

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    continue;

                var data = line[5..].Trim();
                if (string.IsNullOrWhiteSpace(data))
                    continue;

                if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
                    break;

                foreach (var evt in ProcessChunk(data, state))
                {
                    parseableEventCount++;
                    Debug($"response.event={DescribeStreamEvent(evt)}");
                    if (evt is LlmErrorEvent errorEvent)
                    {
                        yield return errorEvent;
                        yield break;
                    }

                    yield return evt;
                }
            }

            if (parseableEventCount == 0)
            {
                yield return new LlmErrorEvent(
                    "Google Antigravity stream produced no parseable events.",
                    LlmErrorCategory.Validation,
                    Retryable: false);
                yield break;
            }

            foreach (var evt in state.FlushThinking())
                yield return evt;

            foreach (var toolState in state.ToolCalls.Values.OrderBy(x => x.Index))
            {
                if (state.CompletedToolIds.Add(toolState.Id))
                    yield return new LlmToolUseCompletedEvent(toolState.Id);
            }

            var finalStopReason = state.StopReason;
            if (finalStopReason == LlmStopReason.Stop && state.ToolCalls.Count > 0)
                finalStopReason = LlmStopReason.ToolUse;

            yield return new LlmCompletedEvent(
                state.TextBuilder.Length == 0 ? null : state.TextBuilder.ToString(),
                state.AllThinkingBuilder.Length == 0 ? null : state.AllThinkingBuilder.ToString(),
                state.ToolCalls.Values
                    .OrderBy(x => x.Index)
                    .Select(x => new ToolCall(x.Id, x.Name, x.ArgumentsBuilder.ToString()))
                    .ToList(),
                state.Usage,
                state.LastThinkingSignature,
                finalStopReason);
        }
    }

    private object BuildRequestPayload(LlmRequest request, IReadOnlyList<LlmMessage> messages)
    {
        var requestBody = new Dictionary<string, object?>();
        requestBody["contents"] = BuildContents(messages, request.Model.ModelId);
        requestBody["sessionId"] = request.SessionId;
        requestBody["systemInstruction"] = BuildSystemInstruction(request.SystemPrompt);

        var generationConfig = BuildGenerationConfig(request);
        if (generationConfig.Count > 0)
            requestBody["generationConfig"] = generationConfig;

        var tools = BuildTools(request.Tools, request.Model.ModelId);
        if (tools != null)
            requestBody["tools"] = tools;

        return new Dictionary<string, object?>
        {
            ["project"] = _projectId,
            ["model"] = request.Model.ModelId,
            ["request"] = requestBody,
            ["requestType"] = "agent",
            ["userAgent"] = "antigravity",
            ["requestId"] = $"agent-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}"[..38]
        };
    }

    private static Dictionary<string, object?> BuildSystemInstruction(string? systemPrompt)
    {
        var parts = new List<Dictionary<string, string>>
        {
            new() { ["text"] = AntigravitySystemInstruction },
            new() { ["text"] = $"Please ignore following [ignore]{AntigravitySystemInstruction}[/ignore]" }
        };

        if (!string.IsNullOrWhiteSpace(systemPrompt))
            parts.Add(new Dictionary<string, string> { ["text"] = systemPrompt });

        return new Dictionary<string, object?>
        {
            ["role"] = "user",
            ["parts"] = parts
        };
    }

    private static Dictionary<string, object?> BuildGenerationConfig(LlmRequest request)
    {
        var config = new Dictionary<string, object?>();
        var maxOutputTokens = request.MaxOutputTokens ?? request.Model.MaxOutputTokens;
        if (maxOutputTokens is > 0)
            config["maxOutputTokens"] = maxOutputTokens;

        if (request.ThinkingLevel == ThinkingLevel.Off)
            return config;

        var thinkingConfig = new Dictionary<string, object?>
        {
            ["includeThoughts"] = true
        };

        if (TryResolveGeminiThinkingLevel(request.Model.ModelId, request.ThinkingLevel, out var level))
        {
            thinkingConfig["thinkingLevel"] = level;
        }
        else
        {
            var budget = ResolveThinkingBudget(request.ThinkingLevel, request.ThinkingBudgets);
            if (budget > 0)
                thinkingConfig["thinkingBudget"] = budget;
        }

        config["thinkingConfig"] = thinkingConfig;
        return config;
    }

    private static IReadOnlyList<Dictionary<string, object?>>? BuildTools(
        IReadOnlyList<ToolDefinition> tools,
        string modelId)
    {
        if (tools.Count == 0)
            return null;

        var useParameters = modelId.StartsWith("claude-", StringComparison.OrdinalIgnoreCase);
        var declarations = new List<Dictionary<string, object?>>(tools.Count);
        foreach (var tool in tools)
        {
            var declaration = new Dictionary<string, object?>
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description
            };

            declaration[useParameters ? "parameters" : "parametersJsonSchema"] = tool.ParametersSchema;
            declarations.Add(declaration);
        }

        return
        [
            new Dictionary<string, object?>
            {
                ["functionDeclarations"] = declarations
            }
        ];
    }

    private static List<Dictionary<string, object?>> BuildContents(
        IReadOnlyList<LlmMessage> messages,
        string modelId)
    {
        var contents = new List<Dictionary<string, object?>>();
        var toolCallIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var nextToolCallIndex = 0;

        string NormalizeToolCallId(string rawId)
        {
            var rawKey = string.IsNullOrWhiteSpace(rawId)
                ? $"call_{nextToolCallIndex}"
                : rawId.Split('|', 2)[0];
            if (toolCallIdMap.TryGetValue(rawKey, out var mapped))
                return mapped;

            mapped = ToolCallIdNormalizer.Normalize(rawKey, nextToolCallIndex);
            toolCallIdMap[rawKey] = mapped;
            nextToolCallIndex++;
            return mapped;
        }

        static List<Dictionary<string, object?>> NewParts() => [];

        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case LlmMessageRole.User:
                    {
                        var parts = NewParts();
                        foreach (var block in message.Content)
                        {
                            switch (block)
                            {
                                case TextContentBlock text when !string.IsNullOrWhiteSpace(text.Text):
                                    parts.Add(new Dictionary<string, object?> { ["text"] = text.Text });
                                    break;
                                case ImageContentBlock image:
                                    parts.Add(new Dictionary<string, object?>
                                    {
                                        ["inlineData"] = new Dictionary<string, object?>
                                        {
                                            ["mimeType"] = image.MimeType,
                                            ["data"] = image.Base64Data
                                        }
                                    });
                                    break;
                            }
                        }

                        if (parts.Count > 0)
                        {
                            contents.Add(new Dictionary<string, object?>
                            {
                                ["role"] = "user",
                                ["parts"] = parts
                            });
                        }

                        break;
                    }
                case LlmMessageRole.Assistant:
                    {
                        var parts = NewParts();
                        foreach (var block in message.Content)
                        {
                            switch (block)
                            {
                                case TextContentBlock text when !string.IsNullOrWhiteSpace(text.Text):
                                    parts.Add(new Dictionary<string, object?> { ["text"] = text.Text });
                                    break;
                                case ThinkingContentBlock thinking when !string.IsNullOrWhiteSpace(thinking.Text):
                                    // Keep replay stable across providers by degrading thinking to plain text.
                                    parts.Add(new Dictionary<string, object?> { ["text"] = thinking.Text });
                                    break;
                                case ToolCallContentBlock toolCall:
                                    {
                                        var normalizedId = NormalizeToolCallId(toolCall.ToolCallId);
                                        parts.Add(new Dictionary<string, object?>
                                        {
                                            ["functionCall"] = new Dictionary<string, object?>
                                            {
                                                ["name"] = toolCall.ToolName,
                                                ["args"] = ParseJsonObject(toolCall.ArgumentsJson),
                                                ["id"] = normalizedId
                                            }
                                        });
                                        break;
                                    }
                            }
                        }

                        if (parts.Count > 0)
                        {
                            contents.Add(new Dictionary<string, object?>
                            {
                                ["role"] = "model",
                                ["parts"] = parts
                            });
                        }

                        break;
                    }
                case LlmMessageRole.Tool:
                    {
                        foreach (var toolResult in message.Content.OfType<ToolResultContentBlock>())
                        {
                            var normalizedId = NormalizeToolCallId(toolResult.ToolCallId);
                            var functionResponsePart = new Dictionary<string, object?>
                            {
                                ["functionResponse"] = new Dictionary<string, object?>
                                {
                                    ["name"] = toolResult.ToolName,
                                    ["response"] = toolResult.IsError
                                        ? new Dictionary<string, object?> { ["error"] = toolResult.ContentText }
                                        : new Dictionary<string, object?> { ["output"] = toolResult.ContentText },
                                    ["id"] = normalizedId
                                }
                            };

                            if (TryGetLastFunctionResponseUserTurn(contents, out var lastParts))
                            {
                                lastParts.Add(functionResponsePart);
                            }
                            else
                            {
                                contents.Add(new Dictionary<string, object?>
                                {
                                    ["role"] = "user",
                                    ["parts"] = new List<Dictionary<string, object?>> { functionResponsePart }
                                });
                            }
                        }

                        break;
                    }
            }
        }

        return contents;
    }

    private static bool TryGetLastFunctionResponseUserTurn(
        List<Dictionary<string, object?>> contents,
        out List<Dictionary<string, object?>> parts)
    {
        parts = [];
        if (contents.Count == 0)
            return false;

        var last = contents[^1];
        if (!last.TryGetValue("role", out var roleValue) || !string.Equals(roleValue as string, "user", StringComparison.Ordinal))
            return false;

        if (!last.TryGetValue("parts", out var partsValue) || partsValue is not List<Dictionary<string, object?>> existingParts || existingParts.Count == 0)
            return false;

        if (!existingParts[^1].ContainsKey("functionResponse"))
            return false;

        parts = existingParts;
        return true;
    }

    private IEnumerable<LlmStreamEvent> ProcessChunk(string data, StreamState state)
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
            if (TryGetProperty(root, "error", out var errorElement))
            {
                var errorMessage = TryGetString(errorElement, "message")
                                   ?? TryGetString(root, "message")
                                   ?? "Google Antigravity stream failed";
                yield return new LlmErrorEvent(errorMessage, LlmErrorCategory.Validation, Retryable: false);
                yield break;
            }

            if (!TryGetProperty(root, "response", out var response))
                yield break;

            if (TryGetProperty(response, "usageMetadata", out var usageMetadata))
                state.Usage = ParseUsage(usageMetadata, state.Pricing);

            var candidate = TryGetFirstCandidate(response);
            if (candidate is null)
                yield break;

            if (TryGetProperty(candidate.Value, "content", out var content)
                && TryGetProperty(content, "parts", out var parts)
                && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    var text = TryGetString(part, "text");
                    if (!string.IsNullOrEmpty(text))
                    {
                        var isThinking = TryGetBoolean(part, "thought");
                        if (isThinking)
                        {
                            if (!state.HasActiveThinking)
                                yield return state.StartThinking();

                            var signature = TryGetString(part, "thoughtSignature");
                            state.AppendThinking(text, signature);
                            yield return new LlmThinkingDeltaEvent(text);
                        }
                        else
                        {
                            foreach (var evt in state.FlushThinking())
                                yield return evt;

                            state.TextBuilder.Append(text);
                            yield return new LlmTextDeltaEvent(text);
                        }
                    }

                    if (TryGetProperty(part, "functionCall", out var functionCall))
                    {
                        foreach (var evt in state.FlushThinking())
                            yield return evt;

                        var rawCallId = TryGetString(functionCall, "id");
                        var toolName = TryGetString(functionCall, "name") ?? string.Empty;
                        var argsJson = TryGetProperty(functionCall, "args", out var args)
                            ? args.GetRawText()
                            : "{}";

                        var created = state.TryGetOrCreateToolCall(rawCallId, toolName, out var toolCall);
                        if (created)
                            yield return new LlmToolUseStartedEvent(toolCall!.Id, toolCall.Name);

                        if (!string.IsNullOrWhiteSpace(argsJson))
                        {
                            toolCall!.ArgumentsBuilder.Clear();
                            toolCall.ArgumentsBuilder.Append(argsJson);
                            yield return new LlmToolUseArgumentsDeltaEvent(toolCall.Id, argsJson);
                        }

                        if (state.CompletedToolIds.Add(toolCall!.Id))
                            yield return new LlmToolUseCompletedEvent(toolCall.Id);
                    }
                }
            }

            var finishReason = TryGetString(candidate.Value, "finishReason");
            if (!string.IsNullOrWhiteSpace(finishReason))
                state.StopReason = MapStopReason(finishReason);
        }
    }

    private static Usage? ParseUsage(JsonElement usageMetadata, ModelPricing? pricing)
    {
        var promptTokens = TryGetInt32(usageMetadata, "promptTokenCount");
        var cacheReadTokens = TryGetInt32(usageMetadata, "cachedContentTokenCount");
        var outputTokens = TryGetInt32(usageMetadata, "candidatesTokenCount") + TryGetInt32(usageMetadata, "thoughtsTokenCount");
        var inputTokens = Math.Max(0, promptTokens - cacheReadTokens);

        if (inputTokens <= 0 && outputTokens <= 0 && cacheReadTokens <= 0)
            return null;

        var usage = new Usage(
            inputTokens,
            outputTokens,
            cacheReadTokens,
            0,
            new CostBreakdown(0, 0, 0, 0, 0));
        return UsageCostCalculator.AttachCost(usage, pricing);
    }

    private static Dictionary<string, object?> ParseJsonObject(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return new Dictionary<string, object?>();

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new Dictionary<string, object?>();

            return JsonSerializer.Deserialize<Dictionary<string, object?>>(doc.RootElement.GetRawText(), RequestJsonOptions)
                   ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }

    private static LlmStopReason MapStopReason(string finishReason)
    {
        return finishReason switch
        {
            "STOP" => LlmStopReason.Stop,
            "MAX_TOKENS" => LlmStopReason.Length,
            _ => LlmStopReason.Error
        };
    }

    private static bool TryGetRetryDelayMs(HttpResponseMessage response, string body, out int retryDelayMs)
    {
        retryDelayMs = 0;
        if (TryGetRetryAfterDelayMs(response, out retryDelayMs))
            return true;

        if (TryExtractBodyRetryDelayMs(body, out retryDelayMs))
            return true;

        return false;
    }

    private static bool TryGetRetryAfterDelayMs(HttpResponseMessage response, out int delayMs)
    {
        delayMs = 0;
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter == null)
            return false;

        if (retryAfter.Delta.HasValue)
        {
            delayMs = Math.Max(0, (int)Math.Ceiling(retryAfter.Delta.Value.TotalMilliseconds));
            return true;
        }

        if (retryAfter.Date.HasValue)
        {
            delayMs = Math.Max(0, (int)Math.Ceiling((retryAfter.Date.Value - DateTimeOffset.UtcNow).TotalMilliseconds));
            return true;
        }

        return false;
    }

    private static bool TryExtractBodyRetryDelayMs(string body, out int delayMs)
    {
        delayMs = 0;
        if (string.IsNullOrWhiteSpace(body))
            return false;

        var durationMatch = Regex.Match(body, "reset after (?:(\\d+)h)?(?:(\\d+)m)?(\\d+(?:\\.\\d+)?)s", RegexOptions.IgnoreCase);
        if (durationMatch.Success)
        {
            var hours = TryParseInt(durationMatch.Groups[1].Value);
            var minutes = TryParseInt(durationMatch.Groups[2].Value);
            var seconds = TryParseDouble(durationMatch.Groups[3].Value);
            delayMs = Math.Max(0, (int)Math.Ceiling((((hours * 60) + minutes) * 60 + seconds) * 1000));
            return delayMs > 0;
        }

        var retryInMatch = Regex.Match(body, "Please retry in ([0-9.]+)(ms|s)", RegexOptions.IgnoreCase);
        if (retryInMatch.Success)
        {
            var value = TryParseDouble(retryInMatch.Groups[1].Value);
            var unit = retryInMatch.Groups[2].Value;
            delayMs = unit.Equals("ms", StringComparison.OrdinalIgnoreCase)
                ? (int)Math.Ceiling(value)
                : (int)Math.Ceiling(value * 1000);
            return delayMs > 0;
        }

        var retryDelayMatch = Regex.Match(body, "\"retryDelay\"\\s*:\\s*\"([0-9.]+)(ms|s)\"", RegexOptions.IgnoreCase);
        if (retryDelayMatch.Success)
        {
            var value = TryParseDouble(retryDelayMatch.Groups[1].Value);
            var unit = retryDelayMatch.Groups[2].Value;
            delayMs = unit.Equals("ms", StringComparison.OrdinalIgnoreCase)
                ? (int)Math.Ceiling(value)
                : (int)Math.Ceiling(value * 1000);
            return delayMs > 0;
        }

        return false;
    }

    private static int TryParseInt(string value)
        => int.TryParse(value, out var parsed) ? parsed : 0;

    private static double TryParseDouble(string value)
        => double.TryParse(value, out var parsed) ? parsed : 0;

    private static bool IsRetryableBody(string body)
        => body.Contains("resource exhausted", StringComparison.OrdinalIgnoreCase)
           || body.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
           || body.Contains("overloaded", StringComparison.OrdinalIgnoreCase)
           || body.Contains("service unavailable", StringComparison.OrdinalIgnoreCase);

    private static bool IsRetryableStatusCode(int statusCode)
        => statusCode is 408 or 429 or >= 500;

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

    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "Unknown error";

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (TryGetProperty(doc.RootElement, "error", out var error)
                && TryGetString(error, "message") is { } message
                && !string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }
        catch (JsonException)
        {
            // ignored
        }

        return body;
    }

    private static bool IsClaudeThinkingModel(string modelId)
    {
        var normalized = modelId.ToLowerInvariant();
        return normalized.Contains("claude", StringComparison.Ordinal) && normalized.Contains("thinking", StringComparison.Ordinal);
    }

    private static string BuildUserAgent()
    {
        var version = Environment.GetEnvironmentVariable("PI_AI_ANTIGRAVITY_VERSION");
        if (string.IsNullOrWhiteSpace(version))
            version = DefaultVersion;

        return $"antigravity/{version} darwin/arm64";
    }

    private static int ResolveThinkingBudget(ThinkingLevel level, ThinkingBudgets? budgets)
    {
        var overrideBudget = budgets?.Resolve(level);
        if (overrideBudget is > 0)
            return overrideBudget.Value;

        return level switch
        {
            ThinkingLevel.Minimal => 1024,
            ThinkingLevel.Low => 2048,
            ThinkingLevel.Medium => 8192,
            ThinkingLevel.High => 16384,
            ThinkingLevel.XHigh => 32768,
            _ => 0
        };
    }

    private static bool TryResolveGeminiThinkingLevel(
        string modelId,
        ThinkingLevel level,
        out string? thinkingLevel)
    {
        var normalized = modelId.ToLowerInvariant();
        if (normalized.Contains("gemini-3-pro", StringComparison.Ordinal))
        {
            thinkingLevel = level switch
            {
                ThinkingLevel.Minimal or ThinkingLevel.Low => "LOW",
                ThinkingLevel.Medium or ThinkingLevel.High or ThinkingLevel.XHigh => "HIGH",
                _ => null
            };
            return thinkingLevel != null;
        }

        if (normalized.Contains("gemini-3-flash", StringComparison.Ordinal))
        {
            thinkingLevel = level switch
            {
                ThinkingLevel.Minimal => "MINIMAL",
                ThinkingLevel.Low => "LOW",
                ThinkingLevel.Medium => "MEDIUM",
                ThinkingLevel.High or ThinkingLevel.XHigh => "HIGH",
                _ => null
            };
            return thinkingLevel != null;
        }

        thinkingLevel = null;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
            return true;

        value = default;
        return false;
    }

    private static JsonElement? TryGetFirstCandidate(JsonElement response)
    {
        if (!TryGetProperty(response, "candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            return null;

        using var enumerator = candidates.EnumerateArray();
        if (!enumerator.MoveNext())
            return null;

        return enumerator.Current;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static bool TryGetBoolean(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
            return false;

        return value.ValueKind == JsonValueKind.True
               || (value.ValueKind == JsonValueKind.String
                   && bool.TryParse(value.GetString(), out var parsed)
                   && parsed);
    }

    private static int TryGetInt32(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
            return number;

        return 0;
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

    private sealed class StreamState
    {
        public StreamState(ModelPricing? pricing)
        {
            Pricing = pricing;
        }

        public StringBuilder TextBuilder { get; } = new();
        public StringBuilder AllThinkingBuilder { get; } = new();
        public StringBuilder? ActiveThinkingBuilder { get; private set; }
        public string? ActiveThinkingSignature { get; private set; }
        public string? LastThinkingSignature { get; private set; }
        public Dictionary<string, MutableToolCall> ToolCalls { get; } = new(StringComparer.Ordinal);
        public HashSet<string> UsedToolIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> CompletedToolIds { get; } = new(StringComparer.Ordinal);
        public int NextToolIndex { get; private set; }
        public Usage? Usage { get; set; }
        public LlmStopReason StopReason { get; set; } = LlmStopReason.Stop;
        public ModelPricing? Pricing { get; }

        public bool HasActiveThinking => ActiveThinkingBuilder != null;

        public LlmThinkingStartedEvent StartThinking()
        {
            ActiveThinkingBuilder = new StringBuilder();
            ActiveThinkingSignature = null;
            return new LlmThinkingStartedEvent();
        }

        public void AppendThinking(string delta, string? signature)
        {
            ActiveThinkingBuilder ??= new StringBuilder();
            ActiveThinkingBuilder.Append(delta);
            AllThinkingBuilder.Append(delta);
            if (!string.IsNullOrWhiteSpace(signature))
            {
                ActiveThinkingSignature = signature;
                LastThinkingSignature = signature;
            }
        }

        public IEnumerable<LlmStreamEvent> FlushThinking()
        {
            if (ActiveThinkingBuilder == null)
                yield break;

            var fullThinking = ActiveThinkingBuilder.ToString();
            yield return new LlmThinkingCompletedEvent(fullThinking, ActiveThinkingSignature);
            ActiveThinkingBuilder = null;
            ActiveThinkingSignature = null;
        }

        public bool TryGetOrCreateToolCall(
            string? rawCallId,
            string toolName,
            out MutableToolCall? toolCall)
        {
            var rawId = string.IsNullOrWhiteSpace(rawCallId)
                ? $"call_{NextToolIndex}"
                : rawCallId.Split('|', 2)[0];

            if (ToolCalls.TryGetValue(rawId, out toolCall))
            {
                if (!string.IsNullOrWhiteSpace(toolName))
                    toolCall.Name = toolName;
                return false;
            }

            var normalized = ToolCallIdNormalizer.Normalize(rawId, NextToolIndex);
            while (!UsedToolIds.Add(normalized))
            {
                normalized = ToolCallIdNormalizer.Normalize($"{rawId}_{NextToolIndex}", NextToolIndex + 1);
            }

            toolCall = new MutableToolCall(NextToolIndex, rawId, normalized)
            {
                Name = toolName
            };
            ToolCalls[rawId] = toolCall;
            NextToolIndex++;
            return true;
        }
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
}
