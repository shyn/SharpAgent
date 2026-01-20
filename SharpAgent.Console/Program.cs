using Microsoft.Extensions.Logging;
using SharpAgent.Console;
using SharpAgent.Core;
using SharpAgent.Core.Configuration;
using SharpAgent.Core.Streaming;
using SharpAgent.Core.Tools;
using Spectre.Console;

// Initialize configuration
var configService = new ConfigurationService();
configService.Load();

// Track tool call execution times and arguments
var toolCallStartTimes = new Dictionary<string, DateTime>();
var toolCallInfo = new Dictionary<string, (string ToolName, string Arguments)>();

var logLevel = Environment.GetEnvironmentVariable("LOG_LEVEL") ?? "Information";

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(Enum.Parse<LogLevel>(logLevel, ignoreCase: true));
});

if (!configService.HasApiKey())
{
    var (providerId, _) = ConfigurationService.ParseModelString(configService.Config.DefaultModel);
    AnsiConsole.MarkupLine($"[red]No API key configured for {providerId.EscapeMarkup()}.[/]");
    AnsiConsole.MarkupLine("Set via environment variables (OPENAI_API_KEY or ANTHROPIC_API_KEY)");
    AnsiConsole.MarkupLine($"Or edit config file: [cyan]{configService.ConfigPath.EscapeMarkup()}[/]") ;
    return;
}

var (httpClient, llmClient) = configService.CreateLlmClient();

var tools = new ITool[] { new CalculatorTool(), new ReadFileTool(), new ListFilesTool(), new BashTool(), new GlobTool(), new GrepTool() };
var agentLogger = loggerFactory.CreateLogger<Agent>();
var agent = new Agent(llmClient, tools, logger: agentLogger);

// Display welcome banner
AnsiConsole.Write(new Rule("[bold purple]SharpAgent[/]")
{
    Style = Style.Parse("purple bold")
});
AnsiConsole.WriteLine();

var grid = new Grid();
grid.AddColumn();
grid.AddColumn();
grid.AddRow(
    $"[bold]Model:[/] [cyan]{configService.GetCurrentModelName().EscapeMarkup()}[/]",
    $"[bold]Log:[/] [cyan]{logLevel.EscapeMarkup()}[/]"
);
grid.AddRow(
    $"[bold]Config:[/] [cyan]{configService.ConfigPath.EscapeMarkup()}[/]",
    ""
);
AnsiConsole.Write(grid);
AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("[dim]Type 'exit' to quit, Ctrl+L to change model[/]");
AnsiConsole.WriteLine();

// Initialize history prompt
var historyPrompt = new HistoryTextPrompt(AnsiConsole.Console, "You: ");

// Main interaction loop
while (true)
{
    var result = historyPrompt.PromptWithResult();

    if (result.IsModelSwitch)
    {
        var models = configService.GetAvailableModels().ToList();
        var currentModel = configService.GetCurrentModelName();
        var selectedIndex = models.IndexOf(currentModel);
        if (selectedIndex < 0) selectedIndex = 0;

        AnsiConsole.MarkupLine("[bold yellow]Select model (↑↓ to navigate, Enter to select, Esc to cancel):[/]");

        var cancelled = false;
        while (true)
        {
            // Display models
            for (var i = 0; i < models.Count; i++)
            {
                var prefix = i == selectedIndex ? "[cyan]> [/]" : "  ";
                var style = i == selectedIndex ? "[cyan]" : "[dim]";
                AnsiConsole.MarkupLine($"{prefix}{style}{models[i].EscapeMarkup()}[/]");
            }

            var key = AnsiConsole.Console.Input.ReadKey(intercept: true)!.Value;

            if (key.Key == ConsoleKey.Escape)
            {
                cancelled = true;
                break;
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                break;
            }
            else if (key.Key == ConsoleKey.UpArrow && selectedIndex > 0)
            {
                selectedIndex--;
            }
            else if (key.Key == ConsoleKey.DownArrow && selectedIndex < models.Count - 1)
            {
                selectedIndex++;
            }

            // Move cursor up to redraw
            AnsiConsole.Write($"\u001b[{models.Count}A");
        }

        if (!cancelled)
        {
            var selected = models[selectedIndex];
            if (selected != currentModel)
            {
                configService.SetCurrentModel(selected);

                // Recreate LLM client and agent with new model
                httpClient.Dispose();
                (httpClient, llmClient) = configService.CreateLlmClient();
                agent = new Agent(llmClient, tools, logger: agentLogger);

                AnsiConsole.MarkupLine($"[green]Switched to {selected.EscapeMarkup()}[/]");
            }
        }
        continue;
    }

    var input = result.Input;

    if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        AnsiConsole.MarkupLine("[dim]Goodbye![/]");
        break;
    }

    // Add input to history for arrow key navigation
    historyPrompt.AddToHistory(input);

    try
    {

        await foreach (var evt in agent.RunStreamingAsync(input))
        {
            switch (evt)
            {
                case AgentStartedEvent started:
                    break;

                case AgentTextDeltaEvent delta:
                    AnsiConsole.MarkupInterpolated($"[white]{delta.Text.EscapeMarkup()}[/]");
                    break;

                case AgentThinkingStartedEvent:
                    AnsiConsole.MarkupLine("\n[bold violet]:brain:Thinking...[/]");
                    break;

                case AgentThinkingDeltaEvent thinking:
                    AnsiConsole.MarkupInterpolated($"[dim violet]{thinking.Thinking.EscapeMarkup()}[/]");
                    break;

                case AgentThinkingCompletedEvent thinkingCompleted:
                    AnsiConsole.WriteLine();
                    break;

                case AgentToolCallStartedEvent toolCall:
                    toolCallStartTimes[toolCall.ToolCallId] = DateTime.UtcNow;
                    toolCallInfo[toolCall.ToolCallId] = (toolCall.ToolName, toolCall.Arguments);

                    // Parse arguments for display
                    var argsDisplay = toolCall.Arguments;
                    if (argsDisplay.StartsWith('{'))
                    {
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(argsDisplay);
                            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                var props = new List<string>();
                                foreach (var prop in doc.RootElement.EnumerateObject())
                                {
                                    var value = prop.Value.ValueKind switch
                                    {
                                        System.Text.Json.JsonValueKind.String => $"\"{prop.Value.GetString() ?? ""}\"",
                                        System.Text.Json.JsonValueKind.Number => prop.Value.ToString(),
                                        System.Text.Json.JsonValueKind.True => "true",
                                        System.Text.Json.JsonValueKind.False => "false",
                                        System.Text.Json.JsonValueKind.Array => $"[{prop.Value.GetArrayLength()} items]",
                                        System.Text.Json.JsonValueKind.Object => "{...}",
                                        _ => prop.Value.ToString()
                                    };
                                    props.Add($"{prop.Name}={value}");
                                }
                                argsDisplay = string.Join(", ", props);
                            }
                        }
                        catch { }
                    }

                    // Truncate raw string before escaping
                    var truncatedArgs = argsDisplay.Length > 60
                        ? argsDisplay[..60] + "..."
                        : argsDisplay;

                    AnsiConsole.WriteLine();
                    AnsiConsole.Write("→ ");
                    AnsiConsole.Write(toolCall.ToolName.EscapeMarkup());
                    AnsiConsole.Write($"({truncatedArgs.EscapeMarkup()})");
                    AnsiConsole.WriteLine();
                    break;

                case AgentToolCallCompletedEvent toolComplete:
                    var elapsed = toolCallStartTimes.TryGetValue(toolComplete.ToolCallId, out var startTime)
                        ? (DateTime.UtcNow - startTime).TotalMilliseconds
                        : 0;
                    toolCallStartTimes.Remove(toolComplete.ToolCallId);

                    var toolName = toolCallInfo.TryGetValue(toolComplete.ToolCallId, out var info)
                        ? info.ToolName
                        : "";
                    toolCallInfo.Remove(toolComplete.ToolCallId);

                    var resultIcon = toolComplete.IsError ? "[bold red]✗[/]" : "[bold green]✓[/]";
                    var timeText = elapsed > 0 ? $" [dim]({elapsed:F0}ms)[/]" : "";

                    // Specialized output formatting
                    if (toolComplete.IsError)
                    {
                        AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: [red]{toolComplete.Result.EscapeMarkup()}[/]");
                    }
                    else
                    {
                        string displayResult;
                        switch (toolName)
                        {
                            case "read_file":
                                var lineCount = toolComplete.Result.Split('\n').Length;
                                displayResult = $"Read {lineCount} lines";
                                break;

                            case "list_files":
                                try
                                {
                                    using var doc = System.Text.Json.JsonDocument.Parse(toolComplete.Result);
                                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                                    {
                                        var count = doc.RootElement.GetArrayLength();
                                        displayResult = $"Found {count} items";
                                    }
                                    else
                                    {
                                        displayResult = toolComplete.Result.Length > 300
                                            ? toolComplete.Result[..300] + "\n[dim]...[/]"
                                            : toolComplete.Result;
                                    }
                                }
                                catch
                                {
                                    displayResult = toolComplete.Result.Length > 300
                                        ? toolComplete.Result[..300] + "\n[dim]...[/]"
                                        : toolComplete.Result;
                                }
                                break;

                            case "glob":
                                try
                                {
                                    using var doc = System.Text.Json.JsonDocument.Parse(toolComplete.Result);
                                    if (doc.RootElement.TryGetProperty("matchCount", out var countProp))
                                    {
                                        displayResult = $"Found {countProp.GetInt32()} files";
                                    }
                                    else
                                    {
                                        displayResult = toolComplete.Result.Length > 300
                                            ? toolComplete.Result[..300] + "\n[dim]...[/]"
                                            : toolComplete.Result;
                                    }
                                }
                                catch
                                {
                                    displayResult = toolComplete.Result.Length > 300
                                        ? toolComplete.Result[..300] + "\n[dim]...[/]"
                                        : toolComplete.Result;
                                }
                                break;

                            case "grep":
                                var grepLines = toolComplete.Result.Split('\n');
                                var fileCount = grepLines
                                    .Where(line => line.Contains(':') && !line.StartsWith("No matches"))
                                    .Select(line => line.Split(':')[0])
                                    .Distinct()
                                    .Count();
                                var matchCount = grepLines.Count(line => line.Contains(':') && !line.StartsWith("No matches"));

                                if (fileCount > 0)
                                {
                                    displayResult = $"Found {matchCount} matches in {fileCount} files";
                                }
                                else if (toolComplete.Result.StartsWith("No matches"))
                                {
                                    displayResult = "No matches found";
                                }
                                else
                                {
                                    displayResult = toolComplete.Result.Length > 300
                                        ? toolComplete.Result[..300] + "\n[dim]...[/]"
                                        : toolComplete.Result;
                                }
                                break;

                            case "bash":
                                var bashLines = toolComplete.Result.Split('\n');
                                displayResult = bashLines.Length > 5
                                    ? string.Join('\n', bashLines.Take(5)) + "\n[dim]...[/]"
                                    : toolComplete.Result;
                                break;

                            default:
                                displayResult = toolComplete.Result.Length > 300
                                    ? toolComplete.Result[..300] + "\n[dim]...[/]"
                                    : toolComplete.Result;
                                break;
                        }

                        // displayResult may contain markup - escape only if it doesn't
                        var hasMarkup = displayResult.Contains("[dim]") || displayResult.Contains("[bold]");
                        var safeResult = displayResult.EscapeMarkup();
                        AnsiConsole.MarkupLine($"  {resultIcon} {toolComplete.ToolCallId.EscapeMarkup()}{timeText}: [white]{safeResult}[/]");
                    }
                    break;

                case AgentCompletedEvent completed:
                    AnsiConsole.WriteLine();
                    var answerPanel = new Panel(
                        new Markup($"[bold white]{completed.FinalAnswer.EscapeMarkup()}[/]")
                    )
                    {
                        Header = new PanelHeader("[bold]Final Answer[/]"),
                        Border = BoxBorder.Rounded,
                        BorderStyle = new Style(foreground: Color.Green),
                        Padding = new Padding(1)
                    };
                    AnsiConsole.Write(answerPanel);
                    break;

                case AgentErrorEvent error:
                    var errorTitle = string.IsNullOrEmpty(error.ExceptionType)
                        ? "[bold]Error[/]"
                        : $"[bold]Error: {error.ExceptionType.EscapeMarkup()}[/]";
                    var errorPanel = new Panel(
                        new Markup($"[red]{error.Message.EscapeMarkup()}[/]")
                    )
                    {
                        Header = new PanelHeader(errorTitle),
                        Border = BoxBorder.Rounded,
                        BorderStyle = new Style(foreground: Color.Red),
                        Padding = new Padding(1)
                    };
                    AnsiConsole.Write(errorPanel);
                    break;
            }
        }
        AnsiConsole.WriteLine();
    }
    catch (Exception ex)
    {
                    AnsiConsole.WriteException(ex);
        var errorTitle = $"[bold]Error: {ex.GetType().Name.EscapeMarkup()}[/]";
        var errorPanel = new Panel(
            new Markup($"[red]{ex.Message.EscapeMarkup()}[/]")
        )
        {
            Header = new PanelHeader(errorTitle),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: Color.Red),
            Padding = new Padding(1)
        };
        AnsiConsole.Write(errorPanel);
    }
}

httpClient.Dispose();
