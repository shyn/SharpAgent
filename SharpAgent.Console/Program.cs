using Microsoft.Extensions.Logging;
using SharpAgent.Core;
using SharpAgent.Core.Configuration;
using SharpAgent.Core.Streaming;
using SharpAgent.Core.Tools;

var configService = new ConfigurationService();
configService.Load();

var logLevel = Environment.GetEnvironmentVariable("LOG_LEVEL") ?? "Information";

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(Enum.Parse<LogLevel>(logLevel, ignoreCase: true))
        .AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
});

if (!configService.HasApiKey())
{
    Console.WriteLine($"No API key configured for {configService.Config.Provider}.");
    Console.WriteLine("Set via environment variables (OPENAI_API_KEY or ANTHROPIC_API_KEY)");
    Console.WriteLine($"Or edit config file: {configService.ConfigPath}");
    return;
}

var (httpClient, llmClient) = configService.CreateLlmClient();

var tools = new ITool[] { new CalculatorTool(), new ReadFileTool(), new ListFilesTool(), new BashTool() };
var agentLogger = loggerFactory.CreateLogger<Agent>();
var agent = new Agent(llmClient, tools, logger: agentLogger);

Console.WriteLine("SharpAgent - Type 'exit' to quit");
Console.WriteLine($"Provider: {configService.Config.Provider} | Model: {configService.GetCurrentModelName()} | Log level: {logLevel}");
Console.WriteLine($"Config: {configService.ConfigPath}");
Console.WriteLine();

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    try
    {
        Console.Write("Agent: ");
        await foreach (var evt in agent.RunStreamingAsync(input))
        {
            switch (evt)
            {
                case AgentTextDeltaEvent delta:
                    Console.Write(delta.Text);
                    break;

                case AgentToolCallStartedEvent toolStart:
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"  → Calling tool: {toolStart.ToolName}");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"    Args: {toolStart.Arguments}");
                    Console.ResetColor();
                    break;

                case AgentToolCallCompletedEvent toolComplete:
                    Console.ForegroundColor = ConsoleColor.Green;
                    var displayResult = toolComplete.Result.Length > 200
                        ? toolComplete.Result[..200] + "..."
                        : toolComplete.Result;
                    Console.WriteLine($"  ✓ Result: {displayResult}");
                    Console.ResetColor();
                    break;

                case AgentCompletedEvent:
                    Console.WriteLine();
                    break;

                case AgentErrorEvent error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\nError: {error.Message}");
                    Console.ResetColor();
                    break;
            }
        }
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

httpClient.Dispose();
