using Sharp.AI;
using Sharp.Core.Configuration;

namespace Sharp.Cli;

internal sealed record CliOptions(
    string? ConfigPath,
    string? Model,
    string? WorkingDirectory,
    string? SessionDirectory,
    string? AgentDirectory,
    string? SessionId,
    ThinkingLevel ThinkingLevel,
    int MaxTurns,
    bool EnableSkills,
    bool DiscoverExtensions,
    bool Force,
    bool JsonOutput,
    bool Debug)
{
    public static CliOptions CreateDefault() => new(
        ConfigPath: null, // null means: try CWD first, then agent dir
        Model: null,
        WorkingDirectory: null,
        SessionDirectory: null,
        AgentDirectory: null,
        SessionId: null,
        ThinkingLevel: ThinkingLevel.Off,
        MaxTurns: 20,
        EnableSkills: true,
        DiscoverExtensions: true,
        Force: false,
        JsonOutput: false,
        Debug: false);
}

internal sealed record CliInvocation(
    string Command,
    CliOptions Options,
    IReadOnlyList<string> Positionals,
    bool ShowHelp)
{
    public static CliInvocation Parse(string[] args)
    {
        if (args.Length == 0)
            return new CliInvocation("repl", CliOptions.CreateDefault(), [], false);

        var commandToken = args[0].Trim();
        if (string.IsNullOrWhiteSpace(commandToken) || IsHelpToken(commandToken))
            return new CliInvocation("help", CliOptions.CreateDefault(), [], true);

        var command = commandToken.ToLowerInvariant();
        var options = CliOptions.CreateDefault();
        var positionals = new List<string>();
        var parseOptions = true;

        for (var i = 1; i < args.Length; i++)
        {
            var token = args[i];

            if (parseOptions && token == "--")
            {
                parseOptions = false;
                continue;
            }

            if (parseOptions && token.StartsWith("--", StringComparison.Ordinal))
            {
                if (token == "--help")
                    return new CliInvocation(command, options, positionals, true);

                switch (token)
                {
                    case "--config":
                        options = options with { ConfigPath = ReadOptionValue(args, ref i, token) };
                        break;
                    case "--model":
                        options = options with { Model = ReadOptionValue(args, ref i, token) };
                        break;
                    case "--workdir":
                        options = options with { WorkingDirectory = ReadOptionValue(args, ref i, token) };
                        break;
                    case "--session-dir":
                        options = options with { SessionDirectory = ReadOptionValue(args, ref i, token) };
                        break;
                    case "--agent-dir":
                        options = options with { AgentDirectory = ReadOptionValue(args, ref i, token) };
                        break;
                    case "--session":
                        options = options with { SessionId = ReadOptionValue(args, ref i, token) };
                        break;
                    case "--thinking":
                        options = options with { ThinkingLevel = ParseThinkingLevel(ReadOptionValue(args, ref i, token)) };
                        break;
                    case "--max-turns":
                        options = options with { MaxTurns = ParsePositiveInt(ReadOptionValue(args, ref i, token), token) };
                        break;
                    case "--no-skills":
                        options = options with { EnableSkills = false };
                        break;
                    case "--no-discover-extensions":
                        options = options with { DiscoverExtensions = false };
                        break;
                    case "--force":
                        options = options with { Force = true };
                        break;
                    case "--json":
                        options = options with { JsonOutput = true };
                        break;
                    case "--debug":
                        options = options with { Debug = true };
                        break;
                    default:
                        throw new CliUsageException($"Unknown option '{token}'");
                }

                continue;
            }

            positionals.Add(token);
        }

        return new CliInvocation(command, options, positionals, false);
    }

    private static string ReadOptionValue(string[] args, ref int index, string optionName)
    {
        var next = index + 1;
        if (next >= args.Length)
            throw new CliUsageException($"Missing value for option '{optionName}'");

        var value = args[next];
        if (string.IsNullOrWhiteSpace(value))
            throw new CliUsageException($"Option '{optionName}' cannot be empty");

        index = next;
        return value;
    }

    private static bool IsHelpToken(string token)
        => token is "-h" or "--help" or "help";

    private static ThinkingLevel ParseThinkingLevel(string value)
    {
        if (!Enum.TryParse<ThinkingLevel>(value, ignoreCase: true, out var level))
            throw new CliUsageException($"Invalid thinking level '{value}'");

        return level;
    }

    private static int ParsePositiveInt(string value, string optionName)
    {
        if (!int.TryParse(value, out var number) || number <= 0)
            throw new CliUsageException($"Option '{optionName}' requires a positive integer");

        return number;
    }
}

internal sealed class CliUsageException : Exception
{
    public CliUsageException(string message)
        : base(message)
    {
    }
}
