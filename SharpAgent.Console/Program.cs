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
AnsiConsole.MarkupLine("[dim]Type 'exit' to quit[/]");
AnsiConsole.WriteLine();

// Initialize history prompt
var historyPrompt = new HistoryTextPrompt(AnsiConsole.Console, "You: ");

// Main interaction loop
while (true)
{
    var input = historyPrompt.Prompt();

    if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        AnsiConsole.MarkupLine("[dim]Goodbye![/]");
        break;
    }

    // Add input to history for arrow key navigation
    historyPrompt.AddToHistory(input);

    try
    {
        AnsiConsole.Write(new Rule("[bold yellow]Agent[/]"));

        await foreach (var evt in agent.RunStreamingAsync(input))
        {
            switch (evt)
            {
                case AgentStartedEvent started:
                    AnsiConsole.MarkupLine($"\n[bold yellow]Goal:[/] {started.Goal.EscapeMarkup()}");
                    break;

                case AgentTextDeltaEvent delta:
                    AnsiConsole.MarkupInterpolated($"[white]{delta.Text.EscapeMarkup()}[/]");
                    break;

                case AgentThinkingDeltaEvent thinking:
                    AnsiConsole.MarkupInterpolated($"[dim violet]{thinking.Thinking.EscapeMarkup()}[/]");
                    break;

                case AgentThinkingCompletedEvent thinkingCompleted:
                    AnsiConsole.WriteLine();
                    break;

                case AgentToolUseStartedEvent toolStart:
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[bold cyan]→ LLM requesting:[/] [cyan]{toolStart.ToolName.EscapeMarkup()}[/]");
                    break;

                case AgentToolUseArgumentsDeltaEvent argsDelta:
                    var partialArgs = argsDelta.PartialJson.Length > 100
                        ? argsDelta.PartialJson[..100] + "..."
                        : argsDelta.PartialJson;
                    AnsiConsole.MarkupLine($"  [dim]Args: {partialArgs.EscapeMarkup()}[/]");
                    break;

                case AgentToolUseCompletedEvent toolComplete:
                    AnsiConsole.WriteLine();
                    break;

                case AgentToolCallStartedEvent toolCall:
                    AnsiConsole.WriteLine();
                    var toolPanel = new Panel(
                        new Markup($"[cyan]{toolCall.Arguments.EscapeMarkup()}[/]")
                    )
                    {
                        Header = new PanelHeader($"[bold]Executing: {toolCall.ToolName.EscapeMarkup()}[/]") ,
                        Border = BoxBorder.Rounded,
                        BorderStyle = new Style(foreground: Color.Cyan),
                        Padding = new Padding(1)
                    };
                    AnsiConsole.Write(toolPanel);
                    break;

                case AgentToolCallCompletedEvent toolComplete:
                    var displayResult = toolComplete.Result.Length > 300
                        ? toolComplete.Result[..300] + "\n[dim]...[/]"
                        : toolComplete.Result;

                    if (toolComplete.IsError)
                    {
                        AnsiConsole.MarkupLine($"[bold red]✗ {toolComplete.ToolCallId.EscapeMarkup()}[/]: [red]{displayResult.EscapeMarkup()}[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[bold green]✓ {toolComplete.ToolCallId.EscapeMarkup()}[/]: [white]{displayResult.EscapeMarkup()}[/]");
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
                    var errorPanel = new Panel(
                        new Markup($"[red]{error.Message.EscapeMarkup()}[/]")
                    )
                    {
                        Header = new PanelHeader("[bold]Error[/]"),
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
        var errorPanel = new Panel(
            new Markup($"[red]{ex.Message.EscapeMarkup()}[/]")
        )
        {
            Header = new PanelHeader("[bold]Error[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: Color.Red),
            Padding = new Padding(1)
        };
        AnsiConsole.Write(errorPanel);
    }
}

httpClient.Dispose();
