using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        string systemPrompt = "You are a helpful assistant. Use tools when needed to accomplish tasks.",
        int maxIterations = 100,
        ILogger<Agent>? logger = null)
    {
        _llmClient = llmClient;
        _tools = tools;
        _toolsByName = tools.ToDictionary(t => t.Name);
        _systemPrompt = systemPrompt;
        _maxIterations = maxIterations;
        _logger = logger ?? NullLogger<Agent>.Instance;
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
        _logger.LogInformation("Starting agent with goal: {Goal}", goal);
        yield return new AgentStartedEvent(goal);

        var messages = new List<Message>
        {
            new(Role.System, _systemPrompt),
            new(Role.User, goal)
        };

        for (var i = 0; i < _maxIterations; i++)
        {
            _logger.LogDebug("Iteration {Iteration}/{MaxIterations}", i + 1, _maxIterations);
            _logger.LogDebug("Sending {MessageCount} messages to LLM", messages.Count);

            var textBuilder = new System.Text.StringBuilder();
            IReadOnlyList<ToolCall>? toolCalls = null;

            await foreach (var llmEvent in _llmClient.StreamCompletionAsync(messages, _tools, ct))
            {
                switch (llmEvent)
                {
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
                        break;
                }
            }

            var content = textBuilder.ToString();
            _logger.LogDebug("LLM response: Content={Content}, ToolCalls={ToolCallCount}",
                content.Length > 100 ? content[..100] + "..." : content,
                toolCalls?.Count ?? 0);

            if (toolCalls is null or { Count: 0 })
            {
                _logger.LogInformation("Agent completed with response length: {Length}", content.Length);
                yield return new AgentCompletedEvent(content);
                yield break;
            }

            messages.Add(new Message(Role.Assistant, content, ToolCalls: toolCalls));

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
                messages.Add(new Message(Role.Tool, result, toolCall.Name, toolCall.Id));
            }
        }

        _logger.LogWarning("Agent exceeded maximum iterations ({MaxIterations})", _maxIterations);
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
