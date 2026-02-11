using System.Runtime.CompilerServices;
using System.Text;
using Sharp.AI;
using Sharp.Core.Compaction;

namespace Sharp.Core;

public sealed class AgentLoop
{
    private readonly ILlmProvider _provider;
    private readonly ToolRuntime _toolRuntime;

    public AgentLoop(ILlmProvider provider, ToolRuntime toolRuntime)
    {
        _provider = provider;
        _toolRuntime = toolRuntime;
    }

    /// <summary>
    /// Checks if compaction is needed and yields a compaction required event if so.
    /// This allows the caller to handle compaction before continuing.
    /// </summary>
    /// <param name="conversation">The current conversation.</param>
    /// <param name="compactionService">Optional compaction service to check thresholds.</param>
    /// <param name="onCompactionRequired">Optional callback when compaction is required.</param>
    /// <returns>True if compaction is required and caller should handle it.</returns>
    public static bool CheckCompaction(
        IReadOnlyList<LlmMessage> conversation,
        CompactionService? compactionService,
        Action<int>? onCompactionRequired = null)
    {
        if (compactionService == null)
            return false;

        var tokenCount = TokenEstimator.EstimateConversationTokens(conversation.ToList(), null);

        // Get context window from settings or use a reasonable default
        var contextWindow = 128000; // Default for many modern models

        if (compactionService.ShouldCompact(tokenCount, contextWindow))
        {
            onCompactionRequired?.Invoke(tokenCount);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Estimates the current token count for the conversation.
    /// </summary>
    public static int EstimateTokens(IReadOnlyList<LlmMessage> conversation, string? systemPrompt = null)
        => TokenEstimator.EstimateConversationTokens(conversation.ToList(), systemPrompt);

    public IAsyncEnumerable<AgentEvent> RunAsync(
        List<LlmMessage> conversation,
        string prompt,
        ModelDescriptor model,
        string systemPrompt,
        ThinkingLevel thinkingLevel,
        int maxTurns,
        Func<LlmMessage, CancellationToken, Task> appendMessage,
        CancellationToken ct = default)
        => RunControlledAsync(
            conversation,
            prompt,
            isContinuation: false,
            model,
            systemPrompt,
            thinkingLevel,
            maxTurns,
            appendMessage,
            ct: ct);

    public async IAsyncEnumerable<AgentEvent> RunControlledAsync(
        List<LlmMessage> conversation,
        string? prompt,
        bool isContinuation,
        ModelDescriptor model,
        string systemPrompt,
        ThinkingLevel thinkingLevel,
        int maxTurns,
        Func<LlmMessage, CancellationToken, Task> appendMessage,
        Func<CancellationToken, Task<IReadOnlyList<LlmMessage>>>? dequeueSteeringMessages = null,
        Func<CancellationToken, Task<IReadOnlyList<LlmMessage>>>? dequeueFollowUpMessages = null,
        Func<IReadOnlyList<LlmMessage>, CancellationToken, Task<IReadOnlyList<LlmMessage>>>? transformContext = null,
        Func<IReadOnlyList<LlmMessage>, CancellationToken, Task<IReadOnlyList<LlmMessage>>>? convertToLlm = null,
        string? sessionId = null,
        int? maxRetryDelayMs = 60000,
        IReadOnlyDictionary<string, string>? requestHeaders = null,
        Action<System.Text.Json.JsonElement>? onPayload = null,
        ThinkingBudgets? thinkingBudgets = null,
        Action<string>? onDebugLog = null,
        CompactionService? compactionService = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new AgentStartedEvent(prompt, isContinuation);

        for (var turn = 0; turn < maxTurns; turn++)
        {
            ct.ThrowIfCancellationRequested();

            // Check if compaction is needed before processing this turn
            if (compactionService != null)
            {
                var tokenCount = TokenEstimator.EstimateConversationTokens(conversation, systemPrompt);
                if (compactionService.ShouldCompact(tokenCount, model.ContextWindow))
                {
                    var threshold = (int)((model.ContextWindow ?? 128000) * compactionService.Settings.ThresholdRatio);
                    yield return new AgentCompactionRequiredEvent(tokenCount, threshold);
                }
            }

            var textBuilder = new StringBuilder();
            var thinkingBuilder = new StringBuilder();
            List<ToolCall> toolCalls = [];
            LlmCompletedEvent? completed = null;

            var transformed = await BuildRequestMessagesSafe(conversation, transformContext, convertToLlm, ct);
            if (transformed.Error != null)
            {
                yield return new AgentErrorEvent($"Context transform failed: {transformed.Error.Message}", LlmErrorCategory.Validation);
                yield break;
            }

            var requestMessages = transformed.Messages!;

            var request = new LlmRequest(
                Model: model,
                SystemPrompt: systemPrompt,
                Messages: requestMessages,
                Tools: _toolRuntime.ToToolDefinitions(),
                ThinkingLevel: thinkingLevel,
                MaxOutputTokens: model.MaxOutputTokens,
                SessionId: sessionId,
                MaxRetryDelayMs: maxRetryDelayMs,
                Headers: requestHeaders,
                OnPayload: onPayload,
                ThinkingBudgets: thinkingBudgets,
                OnDebugLog: onDebugLog);

            await foreach (var streamEvent in _provider.StreamAsync(request, ct).WithCancellation(ct))
            {
                switch (streamEvent)
                {
                    case LlmThinkingStartedEvent:
                        yield return new AgentThinkingStartedEvent();
                        break;
                    case LlmThinkingDeltaEvent thinkingDelta:
                        thinkingBuilder.Append(thinkingDelta.Delta);
                        yield return new AgentThinkingDeltaEvent(thinkingDelta.Delta);
                        break;
                    case LlmThinkingCompletedEvent thinkingCompleted:
                        yield return new AgentThinkingCompletedEvent(thinkingCompleted.FullThinking);
                        break;
                    case LlmTextDeltaEvent textDelta:
                        textBuilder.Append(textDelta.Delta);
                        yield return new AgentTextDeltaEvent(textDelta.Delta);
                        break;
                    case LlmToolUseStartedEvent toolStarted:
                        yield return new AgentToolUseStartedEvent(toolStarted.ToolCallId, toolStarted.ToolName);
                        break;
                    case LlmToolUseArgumentsDeltaEvent argsDelta:
                        yield return new AgentToolUseArgumentsDeltaEvent(argsDelta.ToolCallId, argsDelta.PartialArgumentsJson);
                        break;
                    case LlmToolUseCompletedEvent toolCompleted:
                        yield return new AgentToolUseCompletedEvent(toolCompleted.ToolCallId);
                        break;
                    case LlmCompletedEvent completedEvent:
                        completed = completedEvent;
                        toolCalls = completedEvent.ToolCalls.ToList();
                        break;
                    case LlmErrorEvent errorEvent:
                        var errorAssistantBlocks = new List<ContentBlock>();
                        if (thinkingBuilder.Length > 0)
                            errorAssistantBlocks.Add(new ThinkingContentBlock(thinkingBuilder.ToString()));
                        if (textBuilder.Length > 0)
                            errorAssistantBlocks.Add(new TextContentBlock(textBuilder.ToString()));

                        var errorStopReason = errorEvent.Category == LlmErrorCategory.Aborted
                            ? LlmStopReason.Aborted
                            : LlmStopReason.Error;
                        var errorAssistantMessage = new LlmMessage(
                            LlmMessageRole.Assistant,
                            errorAssistantBlocks,
                            StopReason: errorStopReason,
                            ErrorMessage: errorEvent.Message);

                        conversation.Add(errorAssistantMessage);
                        await appendMessage(errorAssistantMessage, CancellationToken.None);
                        yield return new AgentErrorEvent(errorEvent.Message, errorEvent.Category, errorEvent.StatusCode, errorEvent.Retryable);
                        yield break;
                }
            }

            if (completed == null)
            {
                var streamErrorAssistantBlocks = new List<ContentBlock>();
                if (thinkingBuilder.Length > 0)
                    streamErrorAssistantBlocks.Add(new ThinkingContentBlock(thinkingBuilder.ToString()));
                if (textBuilder.Length > 0)
                    streamErrorAssistantBlocks.Add(new TextContentBlock(textBuilder.ToString()));

                var streamErrorMessage = "Provider stream ended without a completion event";
                var streamErrorAssistantMessage = new LlmMessage(
                    LlmMessageRole.Assistant,
                    streamErrorAssistantBlocks,
                    StopReason: LlmStopReason.Error,
                    ErrorMessage: streamErrorMessage);
                conversation.Add(streamErrorAssistantMessage);
                await appendMessage(streamErrorAssistantMessage, CancellationToken.None);
                yield return new AgentErrorEvent("Provider stream ended without a completion event");
                yield break;
            }

            var fullText = completed.FullText ?? textBuilder.ToString();
            var fullThinking = completed.FullThinking ?? thinkingBuilder.ToString();
            var thinkingSignature = completed.ThinkingSignature;

            var assistantBlocks = new List<ContentBlock>();
            if (!string.IsNullOrEmpty(fullThinking) || !string.IsNullOrEmpty(thinkingSignature))
                assistantBlocks.Add(new ThinkingContentBlock(fullThinking, thinkingSignature));
            if (!string.IsNullOrEmpty(fullText))
                assistantBlocks.Add(new TextContentBlock(fullText));
            assistantBlocks.AddRange(toolCalls.Select(tc => new ToolCallContentBlock(tc.Id, tc.Name, tc.ArgumentsJson, tc.Signature)));

            var assistantMessage = new LlmMessage(
                LlmMessageRole.Assistant,
                assistantBlocks,
                StopReason: completed.StopReason);
            conversation.Add(assistantMessage);
            await appendMessage(assistantMessage, ct);

            if (toolCalls.Count == 0)
            {
                var followUps = dequeueFollowUpMessages == null
                    ? Array.Empty<LlmMessage>()
                    : await dequeueFollowUpMessages(ct);

                if (followUps.Count > 0)
                {
                    foreach (var followUp in followUps)
                    {
                        conversation.Add(followUp);
                        await appendMessage(followUp, ct);
                    }

                    continue;
                }

                yield return new AgentCompletedEvent(assistantMessage);
                yield break;
            }

            var toolMessages = new List<LlmMessage>();
            var interruptedBySteering = false;
            foreach (var call in toolCalls)
            {
                yield return new AgentToolExecutionStartedEvent(call.Id, call.Name, call.ArgumentsJson);

                var partials = new List<ToolInvocationResult>();
                var sync = new object();
                var progress = new Progress<ToolInvocationResult>(partial =>
                {
                    lock (sync)
                    {
                        partials.Add(partial);
                    }
                });

                var result = await _toolRuntime.ExecuteAsync(call, progress, ct);

                List<ToolInvocationResult> snapshot;
                lock (sync)
                {
                    snapshot = partials.ToList();
                }

                foreach (var partial in snapshot)
                    yield return new AgentToolExecutionUpdatedEvent(call.Id, call.Name, partial);

                yield return new AgentToolExecutionCompletedEvent(call.Id, call.Name, result);

                var toolMessage = new LlmMessage(
                    LlmMessageRole.Tool,
                    [result.ToToolResultBlock(call.Id, call.Name)]);

                conversation.Add(toolMessage);
                toolMessages.Add(toolMessage);
                await appendMessage(toolMessage, ct);

                var steeringMessages = dequeueSteeringMessages == null
                    ? Array.Empty<LlmMessage>()
                    : await dequeueSteeringMessages(ct);

                if (steeringMessages.Count > 0)
                {
                    foreach (var steering in steeringMessages)
                    {
                        conversation.Add(steering);
                        await appendMessage(steering, ct);
                    }

                    interruptedBySteering = true;
                    break;
                }
            }

            yield return new AgentTurnCompletedEvent(assistantMessage, toolMessages);

            if (interruptedBySteering)
                continue;
        }

        yield return new AgentErrorEvent($"Agent exceeded max turns ({maxTurns})", LlmErrorCategory.Validation);
    }

    private static async Task<TransformedMessagesResult> BuildRequestMessagesSafe(
        IReadOnlyList<LlmMessage> conversation,
        Func<IReadOnlyList<LlmMessage>, CancellationToken, Task<IReadOnlyList<LlmMessage>>>? transformContext,
        Func<IReadOnlyList<LlmMessage>, CancellationToken, Task<IReadOnlyList<LlmMessage>>>? convertToLlm,
        CancellationToken ct)
    {
        try
        {
            var requestMessages = transformContext == null
                ? conversation
                : await transformContext(conversation, ct);

            if (convertToLlm != null)
                requestMessages = await convertToLlm(requestMessages, ct);

            return new TransformedMessagesResult(requestMessages, null);
        }
        catch (Exception ex)
        {
            return new TransformedMessagesResult(null, ex);
        }
    }

    private sealed record TransformedMessagesResult(IReadOnlyList<LlmMessage>? Messages, Exception? Error);
}
