namespace TinyClaw.Service.Workers;

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using TinyClaw.Core.Configuration;
using TinyClaw.Core.Data;
using TinyClaw.Core.Models;
using TinyClaw.Core.Services;

public class QueueProcessorWorker : BackgroundService
{
    private readonly ILogger<QueueProcessorWorker> _logger;
    private readonly MessageRepository _messages;
    private readonly LogRepository _logs;
    private readonly ConfigManager _config;
    private readonly MessageRouter _router;
    private readonly AgentEngine _agentEngine;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _agentLocks = new();

    public QueueProcessorWorker(
        ILogger<QueueProcessorWorker> logger,
        MessageRepository messages,
        LogRepository logs,
        ConfigManager config,
        MessageRouter router,
        AgentEngine agentEngine)
    {
        _logger = logger;
        _messages = messages;
        _logs = logs;
        _config = config;
        _router = router;
        _agentEngine = agentEngine;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Queue processor started (using Sharp.Core runtime)");
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await ProcessQueueAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Queue processing error");
            }
        }
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        var settings = _config.LoadSettings();
        var agents = _config.GetAgents(settings);
        var teams = _config.GetTeams(settings);

        var pending = _messages.GetByStatus(MessageStatus.Pending, limit: 50);
        if (pending.Count == 0) return;

        var tasks = new List<Task>();
        foreach (var msg in pending)
        {
            if (string.IsNullOrEmpty(msg.AgentId))
            {
                var routing = _router.Route(msg.Content, agents, teams);
                msg.AgentId = routing.AgentId;
                msg.Content = routing.Message;

                if (routing.IsError)
                {
                    _messages.Complete(msg.Id, routing.Message);
                    continue;
                }
            }

            if (!agents.ContainsKey(msg.AgentId ?? ""))
                msg.AgentId = agents.ContainsKey("default") ? "default" : agents.Keys.First();

            var agentId = msg.AgentId!;
            var semaphore = _agentLocks.GetOrAdd(agentId, _ => new SemaphoreSlim(1, 1));

            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    await ProcessSingleMessageAsync(msg, agentId, agents, teams, settings, ct);
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks);
    }

    private async Task ProcessSingleMessageAsync(QueueMessage msg, string agentId,
        Dictionary<string, AgentConfig> agents, Dictionary<string, TeamConfig> teams,
        Settings settings, CancellationToken ct)
    {
        var claimed = _messages.Dequeue(agentId);
        if (claimed == null) return;

        var agent = agents[agentId];
        var workspacePath = settings.Workspace?.Path ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "tinyclaw-workspace");

        _logger.LogInformation("Processing [{Channel}] from {Sender} → @{Agent}", claimed.Channel, claimed.Sender, agentId);

        try
        {
            var teamContext = FindTeamContext(agentId, teams);

            AgentInvokeResult result;

            if (teamContext == null)
            {
                result = await _agentEngine.InvokeAsync(agent, agentId, claimed.Content, workspacePath, false, ct);
            }
            else
            {
                result = await ExecuteTeamChainAsync(agentId, claimed.Content, teamContext.Value.TeamId,
                    teamContext.Value.Team, agents, teams, workspacePath, ct);
            }

            if (!result.Success)
            {
                _logger.LogError("Agent {AgentId} failed: {Error}", agentId, result.Error);
                _messages.Fail(claimed.Id, result.Error ?? "Unknown error");
                return;
            }

            var response = result.Response;
            
            // Truncate if too long
            if (response.Length > 4000)
                response = response[..3900] + "\n\n[Response truncated...]";

            // Serialize files for output
            string? filesOut = result.FilesOut?.Count > 0 
                ? JsonSerializer.Serialize(result.FilesOut) 
                : null;

            _messages.Complete(claimed.Id, response, filesOut);
            _logger.LogInformation("✓ Response ready [{Channel}] {Sender} via @{Agent} ({Length} chars, {Turns} turns)",
                claimed.Channel, claimed.Sender, agentId, response.Length, result.TurnCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for agent {Agent}", agentId);
            _messages.Fail(claimed.Id, ex.Message);
        }
    }

    private (string TeamId, TeamConfig Team)? FindTeamContext(string agentId, Dictionary<string, TeamConfig> teams)
    {
        return _router.FindTeamForAgent(agentId, teams);
    }

    private async Task<AgentInvokeResult> ExecuteTeamChainAsync(string initialAgentId, string message,
        string teamId, TeamConfig team, Dictionary<string, AgentConfig> agents,
        Dictionary<string, TeamConfig> teams, string workspacePath, CancellationToken ct)
    {
        _logger.LogInformation("Team context: {TeamName} (@{TeamId})", team.Name, teamId);

        var chainSteps = new List<(string AgentId, AgentInvokeResult Result)>();
        var currentAgentId = initialAgentId;
        var currentMessage = message;
        var allFiles = new List<string>();

        while (true)
        {
            if (!agents.TryGetValue(currentAgentId, out var currentAgent))
            {
                _logger.LogError("Agent {AgentId} not found during chain execution", currentAgentId);
                break;
            }

            _logger.LogInformation("Chain step {Step}: invoking @{AgentId}", chainSteps.Count + 1, currentAgentId);

            AgentInvokeResult stepResult;
            try
            {
                stepResult = await _agentEngine.InvokeAsync(currentAgent, currentAgentId, currentMessage, workspacePath, false, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chain step error (agent: {AgentId})", currentAgentId);
                stepResult = new AgentInvokeResult
                {
                    Success = false,
                    Response = "Sorry, I encountered an error processing this request.",
                    Error = ex.Message,
                    TurnCount = 0
                };
            }

            chainSteps.Add((currentAgentId, stepResult));

            if (stepResult.FilesOut != null)
                allFiles.AddRange(stepResult.FilesOut);

            // Check if there are teammate mentions to continue the chain
            if (!stepResult.Success || string.IsNullOrWhiteSpace(stepResult.Response))
            {
                _logger.LogInformation("Chain ended after {Steps} step(s) — agent error or empty response", chainSteps.Count);
                break;
            }

            var teammateMentions = _router.ExtractTeammateMentions(
                stepResult.Response, currentAgentId, teamId, teams, agents);

            if (teammateMentions.Count == 0)
            {
                _logger.LogInformation("Chain ended after {Steps} step(s) — no teammate mentioned", chainSteps.Count);
                break;
            }

            if (teammateMentions.Count == 1)
            {
                var mention = teammateMentions[0];
                _logger.LogInformation("@{From} mentioned @{To} — continuing chain", currentAgentId, mention.TeammateId);
                currentAgentId = mention.TeammateId;
                currentMessage = $"[Message from teammate @{chainSteps[^1].AgentId}]:\n{mention.Message}";
            }
            else
            {
                _logger.LogInformation("@{AgentId} mentioned {Count} teammates — fan-out", currentAgentId, teammateMentions.Count);

                var fanOutTasks = teammateMentions.Select(async mention =>
                {
                    if (!agents.TryGetValue(mention.TeammateId, out var mAgent))
                        return (mention.TeammateId, Result: new AgentInvokeResult
                        {
                            Success = false,
                            Response = $"Error: agent {mention.TeammateId} not found",
                            TurnCount = 0
                        });

                    AgentInvokeResult mResult;
                    try
                    {
                        var mMessage = $"[Message from teammate @{currentAgentId}]:\n{mention.Message}";
                        mResult = await _agentEngine.InvokeAsync(mAgent, mention.TeammateId, mMessage, workspacePath, false, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Fan-out error (agent: {AgentId})", mention.TeammateId);
                        mResult = new AgentInvokeResult
                        {
                            Success = false,
                            Response = "Sorry, I encountered an error processing this request.",
                            Error = ex.Message,
                            TurnCount = 0
                        };
                    }

                    return (mention.TeammateId, Result: mResult);
                });

                var fanOutResults = await Task.WhenAll(fanOutTasks);

                foreach (var result in fanOutResults)
                {
                    chainSteps.Add(result);
                    if (result.Result.FilesOut != null)
                        allFiles.AddRange(result.Result.FilesOut);
                }

                _logger.LogInformation("Fan-out complete — {Count} responses collected", fanOutResults.Length);
                break;
            }
        }

        // Combine responses
        string combinedResponse;
        if (chainSteps.Count == 1)
        {
            combinedResponse = chainSteps[0].Result.Response;
        }
        else
        {
            combinedResponse = string.Join("\n\n---\n\n",
                chainSteps.Select(step => $"@{step.AgentId}: {step.Result.Response}"));
        }

        var totalTurns = chainSteps.Sum(s => s.Result.TurnCount);
        var hadError = chainSteps.Any(s => !s.Result.Success);

        return new AgentInvokeResult
        {
            Success = !hadError,
            Response = combinedResponse,
            FilesOut = allFiles.Distinct().ToList(),
            TurnCount = totalTurns,
            Error = hadError ? "One or more agents in the chain encountered errors" : null
        };
    }
}
