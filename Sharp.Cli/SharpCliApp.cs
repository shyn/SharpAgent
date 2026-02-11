using System.Text;
using System.Text.Json;
using Sharp.AI;
using Sharp.Core;
using Sharp.Core.Configuration;
using Sharp.Core.Resources;
using Sharp.Core.Sessions;

namespace Sharp.Cli;

internal static class SharpCliApp
{
    private static readonly JsonSerializerOptions OutputJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        CliInvocation invocation;
        try
        {
            invocation = CliInvocation.Parse(args);
        }
        catch (CliUsageException ex)
        {
            Console.Error.WriteLine($"Argument error: {ex.Message}");
            PrintHelp();
            return 2;
        }

        if (invocation.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return invocation.Command switch
            {
                "run" => await RunOnceAsync(invocation.Options, invocation.Positionals, ct),
                "repl" => await RunReplAsync(invocation.Options, ct),
                "models" => RunModels(invocation.Options),
                "config" => RunConfig(invocation.Options, invocation.Positionals),
                _ => FailUnknownCommand(invocation.Command)
            };
        }
        catch (CliUsageException ex)
        {
            Console.Error.WriteLine($"Argument error: {ex.Message}");
            return 2;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal: {ex.Message}");
            return 1;
        }
    }

    private static int RunModels(CliOptions options)
    {
        var configService = LoadConfigService(options);
        var models = configService.GetAvailableModels();
        foreach (var model in models)
            Console.WriteLine(model);

        return 0;
    }

    private static AgentConfigurationService LoadConfigService(CliOptions options)
    {
        // If explicit path provided, use it; otherwise discover (CWD -> agent dir)
        return options.ConfigPath is not null
            ? AgentConfigurationService.LoadFromFile(options.ConfigPath)
            : AgentConfigurationService.LoadFromFile();
    }

    private static string GetEffectiveConfigPath(CliOptions options)
    {
        // Returns the path that would be used for config operations
        return options.ConfigPath
            ?? Path.Combine(Directory.GetCurrentDirectory(), "config.json");
    }

    private static int RunConfig(CliOptions options, IReadOnlyList<string> positionals)
    {
        if (positionals.Count == 0)
            throw new CliUsageException("config requires a subcommand: init | validate");

        var subCommand = positionals[0].ToLowerInvariant();
        var remaining = positionals.Skip(1).ToArray();

        return subCommand switch
        {
            "init" => RunConfigInit(options, remaining),
            "validate" => RunConfigValidate(options, remaining),
            _ => throw new CliUsageException($"Unknown config subcommand '{subCommand}'")
        };
    }

    private static int RunConfigInit(CliOptions options, IReadOnlyList<string> positionals)
    {
        if (positionals.Count > 0)
            throw new CliUsageException("config init does not accept positional arguments");

        if (options.JsonOutput)
            throw new CliUsageException("--json is only supported for 'config validate'");

        var path = GetEffectiveConfigPath(options);
        var exists = File.Exists(path);
        if (exists && !options.Force)
            throw new CliUsageException($"Config already exists at '{path}'. Use --force to overwrite.");

        var service = new AgentConfigurationService(new AgentConfig());
        service.SaveToFile(path);

        var action = exists ? "Overwrote" : "Initialized";
        Console.WriteLine($"{action} config at '{path}'.");
        Console.WriteLine(
            "Set provider environment variables like SHARP_<PROVIDER_ID>_API_KEY (or <PROVIDER_ID>_API_KEY), " +
            "or update provider apiKey values before running.");
        return 0;
    }

    private static int RunConfigValidate(CliOptions options, IReadOnlyList<string> positionals)
    {
        if (positionals.Count > 0)
            throw new CliUsageException("config validate does not accept positional arguments");

        var path = GetEffectiveConfigPath(options);
        if (!File.Exists(path))
        {
            if (options.JsonOutput)
            {
                WriteConfigValidationJson(
                    path: path,
                    isValid: false,
                    errors: [$"Config file not found: '{path}'."],
                    warnings: []);
                return 1;
            }

            Console.Error.WriteLine($"Config file not found: '{path}'. Run 'sharp config init --config \"{path}\"' first.");
            return 1;
        }

        AgentConfigurationService service;
        try
        {
            service = options.ConfigPath is not null
                ? AgentConfigurationService.LoadFromFile(options.ConfigPath)
                : AgentConfigurationService.LoadFromFile();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            if (options.JsonOutput)
            {
                WriteConfigValidationJson(
                    path: path,
                    isValid: false,
                    errors: [$"Failed to load config: {ex.Message}"],
                    warnings: []);
                return 1;
            }

            Console.Error.WriteLine($"Failed to load config: {ex.Message}");
            return 1;
        }

        var validation = service.ValidateConfig();

        if (options.JsonOutput)
        {
            WriteConfigValidationJson(path, validation.IsValid, validation.Errors, validation.Warnings);
            return validation.IsValid ? 0 : 1;
        }

        if (validation.Warnings.Count > 0)
        {
            Console.WriteLine("Config warnings:");
            foreach (var warning in validation.Warnings)
                Console.WriteLine($"  - {warning}");
        }

        if (!validation.IsValid)
        {
            Console.Error.WriteLine("Config validation failed:");
            foreach (var error in validation.Errors)
                Console.Error.WriteLine($"  - {error}");

            return 1;
        }

        Console.WriteLine($"Config is valid: '{path}'.");
        return 0;
    }

    private static void WriteConfigValidationJson(
        string path,
        bool isValid,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings)
    {
        var report = new ConfigValidationReport(
            Path: path,
            IsValid: isValid,
            Errors: errors,
            Warnings: warnings);
        Console.WriteLine(JsonSerializer.Serialize(report, OutputJsonOptions));
    }

    private static async Task<int> RunOnceAsync(
        CliOptions options,
        IReadOnlyList<string> positionals,
        CancellationToken ct)
    {
        var prompt = await ResolvePromptAsync(positionals, ct);
        if (string.IsNullOrWhiteSpace(prompt))
            throw new CliUsageException("run requires a prompt argument or piped stdin content");

        var runtimeOptions = BuildRuntimeOptions(options);
        using var session = await AgentSession.CreateAsync(runtimeOptions, sessionId: options.SessionId, ct: ct);
        PrintSessionHeader(session);
        PrintDiagnostics(session.ResourceSnapshot.Diagnostics);
        if (options.Debug)
            Console.Error.WriteLine("[debug] llm raw debug logging enabled");

        var renderer = new CliEventRenderer();
        await foreach (var evt in session.PromptAsync(prompt, ct))
            renderer.Render(evt);

        renderer.EndTextLine();
        return 0;
    }

    private static async Task<int> RunReplAsync(CliOptions options, CancellationToken ct)
    {
        var runtimeOptions = BuildRuntimeOptions(options);
        using var session = await AgentSession.CreateAsync(runtimeOptions, sessionId: options.SessionId, ct: ct);
        PrintSessionHeader(session);
        PrintDiagnostics(session.ResourceSnapshot.Diagnostics);
        if (options.Debug)
            Console.Error.WriteLine("[debug] llm raw debug logging enabled");
        PrintReplHelp();

        var renderer = new CliEventRenderer();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Console.Write("> ");
                var line = await Console.In.ReadLineAsync(ct);
                if (line == null)
                    break;

                var input = line.Trim();
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (input.StartsWith(':'))
                {
                    var shouldExit = await HandleReplCommandAsync(input, session, renderer, ct);
                    if (shouldExit)
                        break;

                    continue;
                }

                await foreach (var evt in session.PromptAsync(input, ct))
                    renderer.Render(evt);

                renderer.EndTextLine();
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                renderer.EndTextLine();
                Console.Error.WriteLine("[repl:error] operation was cancelled");
            }
            catch (Exception ex)
            {
                renderer.EndTextLine();
                Console.Error.WriteLine($"[repl:error] {ex.Message}");
            }
        }

        return 0;
    }

    private static async Task<bool> HandleReplCommandAsync(
        string input,
        AgentSession session,
        CliEventRenderer renderer,
        CancellationToken ct)
    {
        var parts = input.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();
        var argument = parts.Length == 2 ? parts[1] : string.Empty;

        switch (command)
        {
            case ":help":
                PrintReplHelp();
                return false;
            case ":exit":
            case ":quit":
                return true;
            case ":continue":
                await foreach (var evt in session.ContinueAsync(ct))
                    renderer.Render(evt);
                renderer.EndTextLine();
                return false;
            case ":reload":
                await session.ReloadExtensionsAsync(ct);
                Console.WriteLine("Extensions reloaded.");
                PrintDiagnostics(session.ResourceSnapshot.Diagnostics);
                return false;
            case ":diag":
                PrintDiagnostics(session.ResourceSnapshot.Diagnostics);
                return false;
            case ":session":
                PrintSessionHeader(session);
                return false;
            case ":tree":
                PrintSessionTree(session.SessionManager);
                return false;
            case ":fork":
                return await HandleForkCommandAsync(argument, session, ct);
            case ":switch":
                return await HandleSwitchCommandAsync(argument, session, ct);
            default:
                Console.WriteLine($"Unknown REPL command '{command}'. Use :help.");
                return false;
        }
    }

    private static async Task<bool> HandleForkCommandAsync(string argument, AgentSession session, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            Console.WriteLine("Usage: :fork <entryId>");
            return false;
        }

        var ok = await session.ForkBranchAsync(argument, ct);
        Console.WriteLine(ok
            ? $"Forked to entry '{argument}'."
            : "Fork cancelled by extension hook.");
        return false;
    }

    private static async Task<bool> HandleSwitchCommandAsync(string argument, AgentSession session, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            Console.WriteLine("Usage: :switch <entryId>");
            return false;
        }

        var ok = await session.SwitchBranchAsync(argument, ct);
        Console.WriteLine(ok
            ? $"Switched to entry '{argument}'."
            : "Switch cancelled by extension hook.");
        return false;
    }

    private static AgentRuntimeOptions BuildRuntimeOptions(CliOptions options)
    {
        var configService = LoadConfigService(options);
        try
        {
            Action<string>? debugLog = null;
            if (options.Debug)
                debugLog = message => Console.Error.WriteLine($"[llm:debug] {message}");

            return configService.BuildRuntimeOptions(
                modelString: options.Model,
                workingDirectory: options.WorkingDirectory,
                sessionDirectory: options.SessionDirectory,
                agentDirectory: options.AgentDirectory,
                thinkingLevel: options.ThinkingLevel,
                enableSkills: options.EnableSkills,
                discoverExtensions: options.DiscoverExtensions,
                maxTurns: options.MaxTurns,
                onDebugLog: debugLog);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Missing API key", StringComparison.Ordinal))
        {
            var configPath = options.ConfigPath ?? $"{Directory.GetCurrentDirectory()}/config.json or {AgentConfigurationService.DefaultAgentDirectory()}/config.json";
            throw new CliUsageException(
                $"{ex.Message}. Set OPENAI_API_KEY/ANTHROPIC_API_KEY or update config at '{configPath}'.");
        }
    }

    private static async Task<string> ResolvePromptAsync(IReadOnlyList<string> positionals, CancellationToken ct)
    {
        if (positionals.Count > 0)
            return string.Join(" ", positionals);

        if (!Console.IsInputRedirected)
            return string.Empty;

        var content = await Console.In.ReadToEndAsync(ct);
        return content.Trim();
    }

    private static int FailUnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'");
        PrintHelp();
        return 2;
    }

    private static void PrintSessionHeader(AgentSession session)
    {
        Console.Error.WriteLine($"[session] id={session.SessionManager.SessionId}");
        Console.Error.WriteLine($"[session] file={session.SessionManager.SessionFilePath}");
        Console.Error.WriteLine($"[session] model={session.Model.ProviderId}/{session.Model.ModelId}");
        Console.Error.WriteLine($"[session] thinking={session.ThinkingLevel}");
        Console.Error.WriteLine($"[session] max_turns={session.MaxTurns}");
        Console.Error.WriteLine($"[session] working_dir={session.SessionManager.Header.WorkingDirectory}");
        if (!string.IsNullOrWhiteSpace(session.SessionManager.CurrentLeafId))
            Console.Error.WriteLine($"[session] leaf={session.SessionManager.CurrentLeafId}");
    }

    private static void PrintDiagnostics(IReadOnlyList<ResourceDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
            return;

        Console.Error.WriteLine("[diagnostics]");
        foreach (var diag in diagnostics)
        {
            var pathSuffix = string.IsNullOrWhiteSpace(diag.Path) ? string.Empty : $" path={diag.Path}";
            Console.Error.WriteLine($"  - {diag.Severity}: {diag.Message}{pathSuffix}");
        }
    }

    private static void PrintSessionTree(SessionManager manager)
    {
        const string RootKey = "__root__";

        var entries = manager.Entries;
        if (entries.Count == 0)
        {
            Console.WriteLine("(empty session)");
            return;
        }

        var childrenByParent = new Dictionary<string, List<SessionEntryEnvelope>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var parentKey = entry.ParentId ?? RootKey;
            if (!childrenByParent.TryGetValue(parentKey, out var children))
            {
                children = [];
                childrenByParent[parentKey] = children;
            }

            children.Add(entry);
        }

        foreach (var bucket in childrenByParent.Values)
            bucket.Sort((a, b) => a.TimestampUtc.CompareTo(b.TimestampUtc));

        if (!childrenByParent.TryGetValue(RootKey, out var roots) || roots.Count == 0)
        {
            Console.WriteLine("(invalid tree: missing roots)");
            return;
        }

        for (var i = 0; i < roots.Count; i++)
            PrintTreeNode(roots[i], manager.CurrentLeafId, childrenByParent, string.Empty, i == roots.Count - 1);
    }

    private static void PrintTreeNode(
        SessionEntryEnvelope node,
        string? currentLeafId,
        IReadOnlyDictionary<string, List<SessionEntryEnvelope>> childrenByParent,
        string prefix,
        bool isLast)
    {
        var branch = isLast ? "\\--" : "|--";
        var marker = string.Equals(node.Id, currentLeafId, StringComparison.Ordinal) ? "*" : " ";
        Console.WriteLine($"{prefix}{branch}{marker} {node.Id} [{node.Type}]");

        if (!childrenByParent.TryGetValue(node.Id, out var children) || children.Count == 0)
            return;

        var childPrefix = prefix + (isLast ? "   " : "|  ");
        for (var i = 0; i < children.Count; i++)
            PrintTreeNode(children[i], currentLeafId, childrenByParent, childPrefix, i == children.Count - 1);
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Sharp.Cli - thin host for Sharp.Core runtime

            Usage:
              sharp run <prompt...> [options]
              sharp repl [options]
              sharp models [options]
              sharp config init [options]
              sharp config validate [options]

            Global options:
              --config <path>               Path to config json (default: ~/Library/Application Support/Sharp/config.json)
              --model <provider/model>      Override default model from config
              --workdir <path>              Working directory
              --session-dir <path>          Session store directory
              --agent-dir <path>            Agent directory (~/.sharp equivalent)
              --session <id>                Reuse an existing session id or set a fixed one
              --thinking <level>            off|minimal|low|medium|high|xhigh
              --max-turns <n>               Max turns per prompt loop
              --force                       Overwrite existing file for 'config init'
              --json                        JSON output for 'config validate'
              --debug                       Enable raw LLM debug logs (url/payload/response)
              --no-skills                   Disable skills loading
              --no-discover-extensions      Disable extension directory discovery
              --help                        Show help

            Runtime output:
              assistant text is written to stdout
              event trace (thinking/tool lifecycle) is written to stderr
            """);
    }

    private static void PrintReplHelp()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Output: assistant text -> stdout, event trace -> stderr");
        builder.AppendLine();
        builder.AppendLine("REPL commands:");
        builder.AppendLine("  :help                 Show this help");
        builder.AppendLine("  :continue             Continue current session from the last state");
        builder.AppendLine("  :reload               Reload extensions and resources");
        builder.AppendLine("  :diag                 Print resource diagnostics");
        builder.AppendLine("  :session              Print current session metadata");
        builder.AppendLine("  :tree                 Print session entry tree");
        builder.AppendLine("  :fork <entryId>       Fork current branch to an entry");
        builder.AppendLine("  :switch <entryId>     Switch current leaf to an entry");
        builder.AppendLine("  :exit / :quit         Exit");
        Console.WriteLine(builder.ToString());
    }

    private sealed record ConfigValidationReport(
        string Path,
        bool IsValid,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings);
}
