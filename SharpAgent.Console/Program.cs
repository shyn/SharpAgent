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

// Track tool call execution times and names
var toolCallStartTimes = new Dictionary<string, DateTime>();
var toolCallNames = new Dictionary<string, string>();
var formatterDispatcher = new ToolFormatterDispatcher();

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

var llmClient = configService.CreateLlmClient();

var tools = new ITool[] { new ReadFileTool(), new ListFilesTool(), new BashTool(), new GlobTool(), new GrepTool(), new EditFileTool() };
var agentLogger = loggerFactory.CreateLogger<Agent>();
var agentOptions = new AgentOptions();
var agent = new Agent(llmClient, tools, agentOptions, agentLogger);

// Session message history - persists across the conversation loop
var sessionMessages = new List<Message>();

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
                llmClient.Dispose();
                llmClient = configService.CreateLlmClient();
                agent = new Agent(llmClient, tools, agentOptions, agentLogger);
                
                // Clear session history when switching models
                sessionMessages.Clear();

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
        await foreach (var evt in agent.RunStreamingAsync(sessionMessages, input))
        {
            switch (evt)
            {
                case AgentStartedEvent started:
                    break;

                case AgentTextDeltaEvent delta:
                    AnsiConsole.MarkupInterpolated($"[white]{delta.Text.EscapeMarkup()}[/]");
                    break;

                case AgentThinkingStartedEvent:
                    AnsiConsole.MarkupLine("\n[bold violet]:brain: Thinking...[/]");
                    break;

                case AgentThinkingDeltaEvent thinking:
                    AnsiConsole.MarkupInterpolated($"[dim violet]{thinking.Thinking.EscapeMarkup()}[/]");
                    break;

                case AgentThinkingCompletedEvent thinkingCompleted:
                    AnsiConsole.WriteLine();
                    break;

                case AgentToolCallStartedEvent toolCall:
                    toolCallStartTimes[toolCall.ToolCallId] = DateTime.UtcNow;
                    toolCallNames[toolCall.ToolCallId] = toolCall.ToolName;
                    formatterDispatcher.RenderStart(toolCall);
                    break;

                case AgentToolCallCompletedEvent toolComplete:
                    var elapsed = toolCallStartTimes.TryGetValue(toolComplete.ToolCallId, out var startTime)
                        ? (DateTime.UtcNow - startTime).TotalMilliseconds
                        : 0;
                    toolCallStartTimes.Remove(toolComplete.ToolCallId);

                    var toolName = toolCallNames.TryGetValue(toolComplete.ToolCallId, out var tn) ? tn : "";
                    toolCallNames.Remove(toolComplete.ToolCallId);

                    formatterDispatcher.RenderCompleted(toolComplete, toolName, elapsed);
                    break;

                case AgentMessagesEvent messagesEvt:
                    sessionMessages.AddRange(messagesEvt.NewMessages);
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

llmClient.Dispose();


