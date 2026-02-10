using Sharp.AI;
using Sharp.Core.Extensions;

namespace Sharp.Core;

public sealed class AgentRuntimeOptions
{
    public required ModelDescriptor Model { get; init; }
    public required string ApiKey { get; init; }
    public required string BaseUrl { get; init; }

    public string WorkingDirectory { get; init; } = Directory.GetCurrentDirectory();
    public string SessionDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".sharp",
        "sessions");

    public string AgentDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".sharp");

    public ThinkingLevel ThinkingLevel { get; init; } = ThinkingLevel.Off;

    public string SystemPrompt { get; init; } =
        "You are a coding agent. Prefer small, verifiable steps and call tools only when needed.";

    public string? AppendSystemPrompt { get; init; }

    public bool DiscoverSystemPromptFile { get; init; } = true;

    public int MaxTurns { get; init; } = 20;

    public bool AllowWriteOutsideWorkspace { get; init; }

    public bool IncludeProjectContextFiles { get; init; } = true;

    public bool EnableSkills { get; init; } = true;

    public bool IncludeDefaultSkills { get; init; } = true;

    public IReadOnlyList<string>? SkillPaths { get; init; }

    public int? MaxRetryDelayMs { get; init; } = 60000;

    public IReadOnlyDictionary<string, string>? RequestHeaders { get; init; }

    public Action<System.Text.Json.JsonElement>? OnPayload { get; init; }

    public Action<string>? OnDebugLog { get; init; }

    public ThinkingBudgets? ThinkingBudgets { get; init; }

    public bool EnableExtensions { get; init; } = true;

    public bool DiscoverExtensions { get; init; } = true;

    public IReadOnlyList<string>? ExtensionPaths { get; init; }

    public IReadOnlyList<IAgentExtension>? Extensions { get; init; }

    public IReadOnlyDictionary<string, string>? ExtensionFlagValues { get; init; }
}
