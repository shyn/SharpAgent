namespace Sharp.AI;

internal static class MessageTransforms
{
    public static IReadOnlyList<LlmMessage> DropIncompleteAssistantTurns(IReadOnlyList<LlmMessage> messages)
    {
        var transformed = new List<LlmMessage>(messages.Count);
        var skippedToolCallIds = new HashSet<string>(StringComparer.Ordinal);
        var changed = false;

        foreach (var message in messages)
        {
            var isIncompleteAssistantTurn = message.Role == LlmMessageRole.Assistant
                                            && (
                                                message.StopReason is LlmStopReason.Aborted or LlmStopReason.Error
                                                || (message.StopReason is null
                                                    && !string.IsNullOrWhiteSpace(message.ErrorMessage)));

            if (isIncompleteAssistantTurn)
            {
                changed = true;
                foreach (var toolCall in message.Content.OfType<ToolCallContentBlock>())
                    skippedToolCallIds.Add(toolCall.ToolCallId);
                continue;
            }

            if (message.Role == LlmMessageRole.Tool)
            {
                var remaining = message.Content
                    .OfType<ToolResultContentBlock>()
                    .Where(block => !skippedToolCallIds.Contains(block.ToolCallId))
                    .Cast<ContentBlock>()
                    .ToList();

                if (remaining.Count != message.Content.Count)
                {
                    changed = true;
                    if (remaining.Count == 0)
                        continue;

                    transformed.Add(message with { Content = remaining });
                    continue;
                }
            }

            transformed.Add(message);
        }

        return changed ? transformed : messages;
    }

    public static IReadOnlyList<LlmMessage> NormalizeToolCallIds(
        IReadOnlyList<LlmMessage> messages,
        Func<string?, int, string> normalizeId)
    {
        var transformed = new List<LlmMessage>(messages.Count);
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var nextIndex = 0;
        var changedAny = false;

        foreach (var message in messages)
        {
            if (message.Role == LlmMessageRole.Assistant)
            {
                var blocks = new List<ContentBlock>(message.Content.Count);
                var changed = false;
                foreach (var block in message.Content)
                {
                    if (block is not ToolCallContentBlock toolCall)
                    {
                        blocks.Add(block);
                        continue;
                    }

                    var key = toolCall.ToolCallId;
                    if (!idMap.TryGetValue(key, out var normalizedId))
                    {
                        normalizedId = normalizeId(toolCall.ToolCallId, nextIndex);
                        idMap[key] = normalizedId;
                        nextIndex++;
                    }

                    if (normalizedId == toolCall.ToolCallId)
                    {
                        blocks.Add(block);
                        continue;
                    }

                    blocks.Add(toolCall with { ToolCallId = normalizedId });
                    changed = true;
                }

                if (changed)
                {
                    transformed.Add(message with { Content = blocks });
                    changedAny = true;
                }
                else
                {
                    transformed.Add(message);
                }

                continue;
            }

            if (message.Role == LlmMessageRole.Tool)
            {
                var blocks = new List<ContentBlock>(message.Content.Count);
                var changed = false;
                foreach (var block in message.Content)
                {
                    if (block is not ToolResultContentBlock toolResult)
                    {
                        blocks.Add(block);
                        continue;
                    }

                    var key = toolResult.ToolCallId;
                    if (!idMap.TryGetValue(key, out var normalizedId))
                    {
                        blocks.Add(block);
                        continue;
                    }

                    if (normalizedId == toolResult.ToolCallId)
                    {
                        blocks.Add(block);
                        continue;
                    }

                    blocks.Add(toolResult with { ToolCallId = normalizedId });
                    changed = true;
                }

                if (changed)
                {
                    transformed.Add(message with { Content = blocks });
                    changedAny = true;
                }
                else
                {
                    transformed.Add(message);
                }

                continue;
            }

            transformed.Add(message);
        }

        return changedAny ? transformed : messages;
    }

    public static IReadOnlyList<LlmMessage> ConvertUnsignedThinkingToText(IReadOnlyList<LlmMessage> messages)
    {
        var transformed = new List<LlmMessage>(messages.Count);
        var changedAny = false;

        foreach (var message in messages)
        {
            if (message.Role != LlmMessageRole.Assistant)
            {
                transformed.Add(message);
                continue;
            }

            var blocks = new List<ContentBlock>(message.Content.Count);
            var changed = false;
            foreach (var block in message.Content)
            {
                if (block is not ThinkingContentBlock thinking)
                {
                    blocks.Add(block);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(thinking.Signature))
                {
                    blocks.Add(block);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(thinking.Text))
                    blocks.Add(new TextContentBlock(thinking.Text));
                changed = true;
            }

            if (changed)
            {
                transformed.Add(message with { Content = blocks });
                changedAny = true;
            }
            else
            {
                transformed.Add(message);
            }
        }

        return changedAny ? transformed : messages;
    }

    public static IReadOnlyList<LlmMessage> ConvertNonAnthropicThinkingSignaturesToText(IReadOnlyList<LlmMessage> messages)
    {
        var transformed = new List<LlmMessage>(messages.Count);
        var changedAny = false;

        foreach (var message in messages)
        {
            if (message.Role != LlmMessageRole.Assistant)
            {
                transformed.Add(message);
                continue;
            }

            var blocks = new List<ContentBlock>(message.Content.Count);
            var changed = false;

            foreach (var block in message.Content)
            {
                if (block is not ThinkingContentBlock thinking
                    || string.IsNullOrWhiteSpace(thinking.Signature))
                {
                    blocks.Add(block);
                    continue;
                }

                if (!ThinkingSignatureInterop.TryNormalizeOpenAiReasoningItem(
                        thinking.Signature,
                        out _,
                        out var summaryText))
                {
                    blocks.Add(block);
                    continue;
                }

                var text = !string.IsNullOrWhiteSpace(thinking.Text) ? thinking.Text : summaryText;
                if (!string.IsNullOrWhiteSpace(text))
                    blocks.Add(new TextContentBlock(text));

                changed = true;
            }

            if (changed)
            {
                transformed.Add(message with { Content = blocks });
                changedAny = true;
            }
            else
            {
                transformed.Add(message);
            }
        }

        return changedAny ? transformed : messages;
    }

    public static IReadOnlyList<LlmMessage> EnsureToolResultContinuity(IReadOnlyList<LlmMessage> messages)
    {
        var transformed = new List<LlmMessage>(messages.Count + 8);
        var pendingToolCalls = new List<ToolCallContentBlock>();
        var resolvedToolCalls = new HashSet<string>(StringComparer.Ordinal);

        void FlushPendingToolResults()
        {
            foreach (var call in pendingToolCalls)
            {
                if (resolvedToolCalls.Contains(call.ToolCallId))
                    continue;

                transformed.Add(new LlmMessage(
                    LlmMessageRole.Tool,
                    [new ToolResultContentBlock(call.ToolCallId, call.ToolName, "No result provided", true)]));
            }

            pendingToolCalls.Clear();
            resolvedToolCalls.Clear();
        }

        foreach (var message in messages)
        {
            if (message.Role == LlmMessageRole.Assistant)
            {
                if (pendingToolCalls.Count > 0)
                    FlushPendingToolResults();

                transformed.Add(message);
                pendingToolCalls = message.Content.OfType<ToolCallContentBlock>().ToList();
                resolvedToolCalls.Clear();
                continue;
            }

            if (message.Role == LlmMessageRole.Tool)
            {
                foreach (var toolResult in message.Content.OfType<ToolResultContentBlock>())
                    resolvedToolCalls.Add(toolResult.ToolCallId);

                transformed.Add(message);
                continue;
            }

            if (pendingToolCalls.Count > 0)
                FlushPendingToolResults();

            transformed.Add(message);
        }

        if (pendingToolCalls.Count > 0)
            FlushPendingToolResults();

        return transformed;
    }

    public static IReadOnlyList<LlmMessage> EnsureAssistantAfterToolResult(IReadOnlyList<LlmMessage> messages)
    {
        if (messages.Count <= 1)
            return messages;

        var transformed = new List<LlmMessage>(messages.Count + 4);
        LlmMessageRole? previousRole = null;

        foreach (var message in messages)
        {
            if (previousRole == LlmMessageRole.Tool && message.Role == LlmMessageRole.User)
            {
                transformed.Add(new LlmMessage(
                    LlmMessageRole.Assistant,
                    [new TextContentBlock(string.Empty)]));
            }

            transformed.Add(message);
            previousRole = message.Role;
        }

        return transformed;
    }
}
