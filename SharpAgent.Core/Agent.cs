using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpAgent.Core.Skills;
using SharpAgent.Core.Streaming;

namespace SharpAgent.Core;

public sealed class Agent : IAgent
{
    private readonly ILlmClient _llmClient;
    private readonly IReadOnlyList<ITool> _tools;
    private readonly Dictionary<string, ITool> _toolsByName;
    private readonly string _systemPrompt;
    private readonly int _maxIterations;
    private readonly ILogger<Agent> _logger;

    public Action<ToolCall>? OnToolCallStarted { get; set; }
    public Action<ToolCall, string>? OnToolCallCompleted { get; set; }

    public Agent(
        ILlmClient llmClient,
        IReadOnlyList<ITool> tools,
        AgentOptions options,
        ILogger<Agent>? logger = null)
    {
        _llmClient = llmClient;
        _logger = logger ?? NullLogger<Agent>.Instance;
        _maxIterations = options.MaxIterations;

        // Build system prompt, optionally including AGENTS.md content
        var agentsMdContent = options.LoadAgentsMd
            ? AgentsMdLoader.LoadAsync(options.WorkingDirectory).GetAwaiter().GetResult()
            : null;

        // Discover and load skills
        IReadOnlyList<SkillMetadata> skills = [];
        if (options.LoadSkills)
        {
            skills = SkillsLoader.DiscoverAsync(options.GetEffectiveSkillDirectories()).GetAwaiter().GetResult();
            _logger.LogDebug("Discovered {SkillCount} skills", skills.Count);
        }

        // Build the final system prompt with skills
        _systemPrompt = BuildSystemPromptWithSkills(options.SystemPrompt, agentsMdContent, skills);

        _tools = tools;
        _toolsByName = _tools.ToDictionary(t => t.Name);

        if (options.WorkingDirectory is not null)
        {
            foreach (var tool in _tools)
            {
                tool.WorkingDirectory = options.WorkingDirectory;
            }
        }
    }

    private static string BuildSystemPromptWithSkills(
        string basePrompt,
        string? agentsMdContent,
        IReadOnlyList<SkillMetadata> skills)
    {
        var prompt = AgentsMdLoader.BuildSystemPrompt(basePrompt, agentsMdContent);

        if (skills.Count > 0)
        {
            var skillsPrompt = SkillsLoader.BuildAvailableSkillsPrompt(skills);
            prompt = $"{prompt}\n\n{skillsPrompt}";
        }

        return prompt;
    }

    public async Task<string> RunAsync(string goal, CancellationToken ct = default)
    {
        string? finalAnswer = null;
        await foreach (var evt in RunStreamingAsync(goal, ct))
        {
            if (evt is AgentCompletedEvent completed)
                finalAnswer = completed.FinalAnswer;
        }
        return finalAnswer ?? string.Empty;
    }

    public async IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(
        string goal, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var initialMessages = new List<Message>
        {
            new(Role.System, _systemPrompt),
            new(Role.User, goal)
        };

        await foreach (var evt in RunCoreAsync(initialMessages, ct))
        {
            yield return evt;
        }
    }

    public async IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(
        IReadOnlyList<Message> existingMessages,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Build messages list: existing + new user message
        var messages = new List<Message>(existingMessages);
        
        // Ensure system prompt is first if not present
        if (messages.Count == 0 || messages[0].Role != Role.System)
        {
            messages.Insert(0, new Message(Role.System, _systemPrompt));
        }
        
        // Add the new user message
        var userMsg = new Message(Role.User, userMessage);
        messages.Add(userMsg);

        yield return new AgentStartedEvent(userMessage);

        // Track new messages for session persistence (starting with user message)
        var newMessages = new List<Message> { userMsg };

        await foreach (var evt in RunCoreAsync(messages, ct, newMessages))
        {
            yield return evt;
        }
    }

    private async IAsyncEnumerable<AgentStreamEvent> RunCoreAsync(
        List<Message> messages,
        [EnumeratorCancellation] CancellationToken ct,
        List<Message>? newMessagesTracker = null)
    {
        // For new conversations, emit started event
        if (newMessagesTracker == null)
        {
            var goal = messages.LastOrDefault(m => m.Role == Role.User)?.Content ?? "";
            yield return new AgentStartedEvent(goal);
        }

        for (var i = 0; i < _maxIterations; i++)
        {
            _logger.LogDebug("Iteration {Iteration}/{MaxIterations}", i + 1, _maxIterations);
            _logger.LogDebug("Sending {MessageCount} messages to LLM", messages.Count);

            var textBuilder = new System.Text.StringBuilder();
            var thinkingBuilder = new System.Text.StringBuilder();
            IReadOnlyList<ToolCall>? toolCalls = null;
            string? thinking = null;

            await foreach (var llmEvent in _llmClient.StreamCompletionAsync(messages, _tools, ct))
            {
                switch (llmEvent)
                {
                    case LlmThinkingStartedEvent:
                        yield return new AgentThinkingStartedEvent();
                        break;
                    case LlmThinkingDeltaEvent thinkingDelta:
                        thinkingBuilder.Append(thinkingDelta.Thinking);
                        yield return new AgentThinkingDeltaEvent(thinkingDelta.Thinking);
                        break;
                    case LlmThinkingCompletedEvent thinkingCompleted:
                        yield return new AgentThinkingCompletedEvent(thinkingCompleted.FullThinking);
                        break;
                    case LlmTextDeltaEvent delta:
                        textBuilder.Append(delta.Text);
                        yield return new AgentTextDeltaEvent(delta.Text);
                        break;
                    case LlmToolUseStartedEvent toolStart:
                        yield return new AgentToolUseStartedEvent(toolStart.Id, toolStart.Name);
                        break;
                    case LlmToolUseArgumentsDeltaEvent argsDelta:
                        yield return new AgentToolUseArgumentsDeltaEvent(argsDelta.Id, argsDelta.PartialJson);
                        break;
                    case LlmToolUseCompletedEvent toolComplete:
                        yield return new AgentToolUseCompletedEvent(toolComplete.Id);
                        break;
                    case LlmMessageCompletedEvent completed:
                        toolCalls = completed.ToolCalls;
                        thinking = completed.FullThinking;
                        break;
                }
            }

            var content = textBuilder.ToString();
            var thinkingContent = thinkingBuilder.ToString();
            _logger.LogDebug("LLM response: Content={Content}, ToolCalls={ToolCallCount}",
                content.Length > 100 ? content[..100] + "..." : content,
                toolCalls?.Count ?? 0);

            if (toolCalls is null or { Count: 0 })
            {
                _logger.LogInformation("Agent completed with response length: {Length}", content.Length);
                
                // Track assistant message
                var assistantMsg = new Message(Role.Assistant, content, Thinking: thinkingContent);
                newMessagesTracker?.Add(assistantMsg);
                
                // Emit new messages for session persistence
                if (newMessagesTracker != null)
                {
                    yield return new AgentMessagesEvent(newMessagesTracker);
                }
                
                yield return new AgentCompletedEvent(content);
                yield break;
            }

            var assistantMessage = new Message(Role.Assistant, content, ToolCalls: toolCalls, Thinking: thinkingContent);
            messages.Add(assistantMessage);
            newMessagesTracker?.Add(assistantMessage);

            foreach (var toolCall in toolCalls)
            {
                _logger.LogDebug("Executing tool: {ToolName} with args: {Args}", toolCall.Name, toolCall.Arguments);
                OnToolCallStarted?.Invoke(toolCall);
                yield return new AgentToolCallStartedEvent(toolCall.Id, toolCall.Name, toolCall.Arguments);

                var result = await ExecuteToolAsync(toolCall, ct);
                var isError = result.StartsWith("Error:");

                OnToolCallCompleted?.Invoke(toolCall, result);
                yield return new AgentToolCallCompletedEvent(toolCall.Id, result, isError);

                _logger.LogDebug("Tool result: {Result}", result.Length > 200 ? result[..200] + "..." : result);
                var toolResultMsg = new Message(Role.Tool, result, toolCall.Name, toolCall.Id);
                messages.Add(toolResultMsg);
                newMessagesTracker?.Add(toolResultMsg);
            }
        }

        _logger.LogWarning("Agent exceeded maximum iterations ({MaxIterations})", _maxIterations);
        
        // Emit new messages even on error
        if (newMessagesTracker != null)
        {
            yield return new AgentMessagesEvent(newMessagesTracker);
        }
        
        yield return new AgentErrorEvent($"Agent exceeded maximum iterations ({_maxIterations})");
    }

    private async Task<string> ExecuteToolAsync(ToolCall toolCall, CancellationToken ct)
    {
        if (!_toolsByName.TryGetValue(toolCall.Name, out var tool))
        {
            _logger.LogWarning("Unknown tool requested: {ToolName}", toolCall.Name);
            return $"Error: Unknown tool '{toolCall.Name}'";
        }

        try
        {
            return await tool.ExecuteAsync(toolCall.Arguments, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", toolCall.Name);
            return $"Error executing tool: {ex.Message}";
        }
    }
}
