namespace TinyClaw.Core.Services;

using System.Collections.Concurrent;
using Sharp.AI;
using Sharp.AI.Factories;
using Sharp.AI.Models;
using Sharp.Core;
using Sharp.Core.Sessions;
using TinyClaw.Core.Configuration;
using TinyClaw.Core.Models;

/// <summary>
/// Result of invoking an agent via Sharp.Core.
/// </summary>
public sealed record AgentInvokeResult
{
    public required bool Success { get; init; }
    public required string Response { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string>? FilesOut { get; init; }
    public int TurnCount { get; init; }
}

/// <summary>
/// Agent engine that uses Sharp.Core as the runtime instead of CLI processes.
/// Manages AgentSession lifecycle and handles streaming events.
/// </summary>
public sealed class AgentEngine : IDisposable
{
    private readonly ConfigManager _config;
    private readonly Action<string, object?>? _logDebug;
    private readonly Action<string, object?>? _logInfo;
    private readonly Action<string, object?>? _logError;

    // Cache of active sessions per agent
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();

    public AgentEngine(ConfigManager config)
    {
        _config = config;
    }

    public AgentEngine(ConfigManager config, Action<string, object?> logInfo, Action<string, object?> logDebug, Action<string, object?> logError)
    {
        _config = config;
        _logInfo = logInfo;
        _logDebug = logDebug;
        _logError = logError;
    }

    /// <summary>
    /// Invoke an agent with a message. Creates or reuses an existing session.
    /// </summary>
    public async Task<AgentInvokeResult> InvokeAsync(
        AgentConfig agent,
        string agentId,
        string message,
        string workspacePath,
        bool shouldReset,
        CancellationToken ct = default)
    {
        var sessionLock = _sessionLocks.GetOrAdd(agentId, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(ct);

        try
        {
            // Get or create session
            var session = await GetOrCreateSessionAsync(agent, agentId, workspacePath, shouldReset, ct);

            var responseBuilder = new System.Text.StringBuilder();
            var filesOut = new List<string>();
            var turnCount = 0;
            Exception? capturedError = null;

            // Stream events from the agent
            await foreach (var evt in session.PromptAsync(message, ct))
            {
                switch (evt)
                {
                    case AgentTextDeltaEvent textDelta:
                        responseBuilder.Append(textDelta.Delta);
                        break;

                    case AgentThinkingStartedEvent:
                        _logDebug?.Invoke("Agent {AgentId} started thinking", agentId);
                        break;

                    case AgentThinkingDeltaEvent thinkingDelta:
                        // Optionally include thinking in response or log it
                        _logDebug?.Invoke("Agent {AgentId} thinking: {Delta}", new { AgentId = agentId, thinkingDelta.Delta });
                        break;

                    case AgentToolExecutionStartedEvent toolStarted:
                        _logInfo?.Invoke("Agent {AgentId} executing tool: {ToolName}",
                            new { AgentId = agentId, toolStarted.ToolName });
                        break;

                    case AgentToolExecutionCompletedEvent toolCompleted:
                        // Track file outputs from tool results
                        if (toolCompleted.Result.Details?.TryGetProperty("path", out var pathElement) == true
                            && pathElement.ValueKind == System.Text.Json.JsonValueKind.String
                            && pathElement.GetString() is string filePath
                            && System.IO.File.Exists(filePath))
                        {
                            filesOut.Add(filePath);
                        }
                        break;

                    case AgentTurnCompletedEvent turnCompleted:
                        turnCount++;
                        _logDebug?.Invoke("Agent {AgentId} completed turn {Turn}", new { AgentId = agentId, Turn = turnCount });
                        break;

                    case AgentCompletedEvent:
                        _logInfo?.Invoke("Agent {AgentId} completed response", agentId);
                        break;

                    case AgentErrorEvent errorEvent:
                        capturedError = new InvalidOperationException(
                            $"Agent error: {errorEvent.Message} (Category: {errorEvent.Category})");
                        _logError?.Invoke("Agent {AgentId} encountered error", capturedError);
                        break;

                    case AgentCompactionRequiredEvent compaction:
                        _logInfo?.Invoke("Agent {AgentId} requires compaction at {Tokens} tokens",
                            new { AgentId = agentId, compaction.TokenCount });
                        // Compaction is handled internally by Sharp.Core, but we log it
                        break;
                }
            }

            if (capturedError != null)
            {
                return new AgentInvokeResult
                {
                    Success = false,
                    Response = responseBuilder.ToString(),
                    Error = capturedError.Message,
                    TurnCount = turnCount
                };
            }

            return new AgentInvokeResult
            {
                Success = true,
                Response = responseBuilder.ToString(),
                TurnCount = turnCount,
                FilesOut = filesOut
            };
        }
        catch (Exception ex)
        {
            _logError?.Invoke($"Failed to invoke agent {agentId}: {ex.Message}", ex);
            return new AgentInvokeResult
            {
                Success = false,
                Response = string.Empty,
                Error = ex.Message,
                TurnCount = 0
            };
        }
        finally
        {
            sessionLock.Release();
        }
    }

    /// <summary>
    /// Reset an agent's session (clear conversation history).
    /// </summary>
    public async Task ResetSessionAsync(string agentId, CancellationToken ct = default)
    {
        if (_sessions.TryRemove(agentId, out var session))
        {
            session.Dispose();
            _logInfo?.Invoke("Agent {AgentId} session reset", agentId);
        }

        // Also clear the session file if it exists
        var settings = _config.LoadSettings();
        var agent = GetAgentConfig(agentId, settings);
        if (agent?.SessionId != null)
        {
            var sessionDir = GetSessionDirectory(settings);
            var sessionFile = Path.Combine(sessionDir, $"{agent.SessionId}.jsonl");
            if (System.IO.File.Exists(sessionFile))
            {
                System.IO.File.Delete(sessionFile);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Dispose all active sessions.
    /// </summary>
    public void Dispose()
    {
        foreach (var (agentId, session) in _sessions)
        {
            try
            {
                session.Dispose();
                _logDebug?.Invoke("Disposed session for agent {AgentId}", agentId);
            }
            catch (Exception ex)
            {
                _logError?.Invoke($"Error disposing session for agent {agentId}: {ex.Message}", ex);
            }
        }
        _sessions.Clear();

        foreach (var sem in _sessionLocks.Values)
        {
            sem.Dispose();
        }
        _sessionLocks.Clear();
    }

    private async Task<AgentSession> GetOrCreateSessionAsync(
        AgentConfig agent,
        string agentId,
        string workspacePath,
        bool shouldReset,
        CancellationToken ct)
    {
        // If reset requested, dispose existing session
        if (shouldReset && _sessions.TryRemove(agentId, out var existingSession))
        {
            existingSession.Dispose();
            _logInfo?.Invoke("Agent {AgentId} session reset due to user request", agentId);
        }

        // Try to get existing session
        if (_sessions.TryGetValue(agentId, out var session))
        {
            return session;
        }

        // Create new session
        var settings = _config.LoadSettings();
        var options = BuildAgentRuntimeOptions(agent, agentId, workspacePath, settings);

        session = await AgentSession.CreateAsync(options, ct: ct);

        if (_sessions.TryAdd(agentId, session))
        {
            _logInfo?.Invoke("Created new session for agent {AgentId} (SessionId: {SessionId})",
                new { AgentId = agentId, SessionId = session.SessionManager.SessionId });
        }
        else
        {
            // Another thread created it, dispose ours and use theirs
            session.Dispose();
            session = _sessions[agentId];
        }

        return session;
    }

    private AgentRuntimeOptions BuildAgentRuntimeOptions(
        AgentConfig agent,
        string agentId,
        string workspacePath,
        Settings settings)
    {
        var workingDir = Path.IsPathRooted(agent.WorkingDirectory)
            ? agent.WorkingDirectory
            : Path.Combine(workspacePath, agent.WorkingDirectory);

        Directory.CreateDirectory(workingDir);

        // Build model descriptor
        var modelDescriptor = BuildModelDescriptor(agent);

        // Get API key (agent-specific > global > environment)
        var apiKey = GetApiKey(agent, settings, agent.Provider);

        // Get base URL
        var baseUrl = GetBaseUrl(agent, agent.Provider);

        // Parse thinking level
        var thinkingLevel = ParseThinkingLevel(agent.ThinkingLevel);

        // Get system prompt (SOUL.md or config)
        var systemPrompt = GetSystemPrompt(agent, workingDir);

        // Get session directory
        var sessionDir = GetSessionDirectory(settings);
        Directory.CreateDirectory(sessionDir);

        return new AgentRuntimeOptions
        {
            Model = modelDescriptor,
            ApiKey = apiKey,
            BaseUrl = baseUrl,
            WorkingDirectory = workingDir,
            SessionDirectory = sessionDir,
            SystemPrompt = systemPrompt,
            ThinkingLevel = thinkingLevel,
            MaxTurns = agent.MaxTurns,
            AllowWriteOutsideWorkspace = agent.AllowWriteOutsideWorkspace,
            EnableExtensions = agent.EnableExtensions,
            EnableSkills = true,
            IncludeDefaultSkills = true,
            IncludeProjectContextFiles = true,
            DiscoverSystemPromptFile = false, // We handle SOUL.md manually
            MaxRetryDelayMs = 60000,
            OnDebugLog = msg => _logDebug?.Invoke($"[Agent {agentId}] {msg}", null)
        };
    }

    private ModelDescriptor BuildModelDescriptor(AgentConfig agent)
    {
        var providerId = agent.Provider.ToLowerInvariant();
        var modelId = agent.Model.ToLowerInvariant();

        // Map provider to API kind
        var apiKind = providerId switch
        {
            "anthropic" or "claude" => ProviderApiKind.AnthropicMessages,
            "openai" => ProviderApiKind.OpenAiChatCompletions,
            "openai-responses" => ProviderApiKind.OpenAiResponses,
            _ => ProviderApiKind.OpenAiChatCompletions // Default
        };

        // Resolve full model ID
        var resolvedModelId = ResolveModelId(providerId, modelId);

        // Get context window based on model
        var (contextWindow, maxOutputTokens) = GetModelCapabilities(providerId, resolvedModelId);

        return new ModelDescriptor(
            ProviderId: providerId,
            ModelId: resolvedModelId,
            ApiKind: apiKind,
            ContextWindow: contextWindow,
            MaxOutputTokens: maxOutputTokens
        );
    }

    private string ResolveModelId(string provider, string model)
    {
        // Anthropic model mappings
        if (provider is "anthropic" or "claude")
        {
            return model switch
            {
                "sonnet" or "sonnet-4" => "claude-sonnet-4-5",
                "opus" or "opus-4" => "claude-opus-4-6",
                "haiku" => "claude-3-5-haiku-latest",
                "sonnet-3-5" => "claude-3-5-sonnet-latest",
                _ => model
            };
        }

        // OpenAI model mappings
        if (provider == "openai")
        {
            return model switch
            {
                "gpt-4" => "gpt-4o",
                "gpt-4-turbo" => "gpt-4-turbo-preview",
                "gpt-3.5" => "gpt-3.5-turbo",
                _ => model
            };
        }

        return model;
    }

    private (int? ContextWindow, int? MaxOutputTokens) GetModelCapabilities(string provider, string modelId)
    {
        if (provider is "anthropic" or "claude")
        {
            return modelId switch
            {
                var m when m.Contains("opus") => (200000, 4096),
                var m when m.Contains("sonnet") => (200000, 4096),
                var m when m.Contains("haiku") => (200000, 4096),
                _ => (200000, 4096)
            };
        }

        if (provider == "openai")
        {
            return modelId switch
            {
                var m when m.Contains("gpt-4o") => (128000, 4096),
                var m when m.Contains("gpt-4-turbo") => (128000, 4096),
                var m when m.Contains("gpt-4") => (8192, 4096),
                var m when m.Contains("gpt-3.5") => (16385, 4096),
                _ => (128000, 4096)
            };
        }

        return (128000, 4096);
    }

    private string GetApiKey(AgentConfig agent, Settings settings, string provider)
    {
        // 1. Try agent-specific API key
        if (!string.IsNullOrWhiteSpace(agent.ApiKey))
            return agent.ApiKey;

        // 2. Try global API keys from settings
        if (settings.Models?.ApiKeys?.TryGetValue(provider, out var globalKey) == true
            && !string.IsNullOrWhiteSpace(globalKey))
            return globalKey;

        // 3. Try environment variables
        var envVarName = provider.ToLowerInvariant() switch
        {
            "anthropic" or "claude" => "ANTHROPIC_API_KEY",
            "openai" => "OPENAI_API_KEY",
            _ => $"{provider.ToUpperInvariant()}_API_KEY"
        };

        var envKey = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrWhiteSpace(envKey))
            return envKey;

        throw new InvalidOperationException(
            $"No API key found for provider '{provider}'. " +
            $"Set it in agent config, global config, or environment variable {envVarName}.");
    }

    private string GetBaseUrl(AgentConfig agent, string provider)
    {
        // Use agent-specific base URL if provided
        if (!string.IsNullOrWhiteSpace(agent.BaseUrl))
            return agent.BaseUrl;

        // Default base URLs
        return provider.ToLowerInvariant() switch
        {
            "anthropic" or "claude" => "https://api.anthropic.com/",
            "openai" => "https://api.openai.com/",
            _ => "https://api.openai.com/"
        };
    }

    private ThinkingLevel ParseThinkingLevel(string? level)
    {
        return (level?.ToLowerInvariant()) switch
        {
            "off" or null => ThinkingLevel.Off,
            "minimal" => ThinkingLevel.Minimal,
            "low" => ThinkingLevel.Low,
            "medium" => ThinkingLevel.Medium,
            "high" => ThinkingLevel.High,
            "xhigh" or "x-high" or "extreme" or "maximum" => ThinkingLevel.XHigh,
            _ => ThinkingLevel.Off
        };
    }

    private string GetSystemPrompt(AgentConfig agent, string workingDir)
    {
        // 1. Try agent config override
        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
            return agent.SystemPrompt;

        // 2. Try SOUL.md file
        var soulPath = Path.Combine(workingDir, ".tinyclaw", "SOUL.md");
        if (System.IO.File.Exists(soulPath))
        {
            var soulContent = System.IO.File.ReadAllText(soulPath);
            if (!string.IsNullOrWhiteSpace(soulContent))
                return soulContent.Trim();
        }

        // 3. Default prompt
        return $"You are {agent.Name}, an AI assistant. Help the user with their tasks.";
    }

    private string GetSessionDirectory(Settings settings)
    {
        var baseDir = settings.Workspace?.Path
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "tinyclaw-workspace");
        return Path.Combine(baseDir, ".sessions");
    }

    private AgentConfig? GetAgentConfig(string agentId, Settings settings)
    {
        var agents = _config.GetAgents(settings);
        return agents.TryGetValue(agentId, out var agent) ? agent : null;
    }
}
