using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        _logger.LogInformation("Starting agent with goal: {Goal}", goal);

        var messages = new List<Message>
        {
            new(Role.System, _systemPrompt),
            new(Role.User, goal)
        };

        for (var i = 0; i < _maxIterations; i++)
        {
            _logger.LogDebug("Iteration {Iteration}/{MaxIterations}", i + 1, _maxIterations);
            _logger.LogDebug("Sending {MessageCount} messages to LLM", messages.Count);

            var response = await _llmClient.GetCompletionAsync(messages, _tools, ct);

            _logger.LogDebug("LLM response: Content={Content}, ToolCalls={ToolCallCount}",
                response.Content?.Length > 100 ? response.Content[..100] + "..." : response.Content,
                response.ToolCalls?.Count ?? 0);

            if (!response.HasToolCalls)
            {
                _logger.LogInformation("Agent completed with response length: {Length}", response.Content?.Length ?? 0);
                return response.Content ?? string.Empty;
            }

            messages.Add(new Message(Role.Assistant, response.Content ?? string.Empty, ToolCalls: response.ToolCalls));

            foreach (var toolCall in response.ToolCalls!)
            {
                _logger.LogDebug("Executing tool: {ToolName} with args: {Args}", toolCall.Name, toolCall.Arguments);
                OnToolCallStarted?.Invoke(toolCall);
                var result = await ExecuteToolAsync(toolCall, ct);
                OnToolCallCompleted?.Invoke(toolCall, result);
                _logger.LogDebug("Tool result: {Result}", result.Length > 200 ? result[..200] + "..." : result);
                messages.Add(new Message(Role.Tool, result, toolCall.Name, toolCall.Id));
            }
        }

        _logger.LogWarning("Agent exceeded maximum iterations ({MaxIterations})", _maxIterations);
        throw new InvalidOperationException($"Agent exceeded maximum iterations ({_maxIterations})");
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
