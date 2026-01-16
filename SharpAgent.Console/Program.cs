using Microsoft.Extensions.Logging;
using SharpAgent.Core;
using SharpAgent.Core.Streaming;
using SharpAgent.Core.Tools;

var provider = Environment.GetEnvironmentVariable("LLM_PROVIDER")?.ToLowerInvariant() ?? "openai";
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

var agentLogger = loggerFactory.CreateLogger<Agent>();
HttpClient httpClient;
ILlmClient llmClient;
string modelName;

if (provider == "anthropic")
{
    var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
        ?? throw new InvalidOperationException("Set ANTHROPIC_API_KEY environment variable");
    var baseUrl = Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL") ?? "https://api.anthropic.com/v1/";
    if (!baseUrl.EndsWith('/')) baseUrl += "/";
    var model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-sonnet-4-20250514";
    modelName = model;

    httpClient = new HttpClient
    {
        BaseAddress = new Uri(baseUrl),
        DefaultRequestHeaders =
        {
            { "x-api-key", apiKey },
            { "anthropic-version", "2023-06-01" }
        }
    };

    var clientLogger = loggerFactory.CreateLogger<AnthropicClient>();
    llmClient = new AnthropicClient(httpClient, model, logger: clientLogger);
}
else
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("Set OPENAI_API_KEY environment variable");
    var baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1/";
    if (!baseUrl.EndsWith('/')) baseUrl += "/";
    var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
    modelName = model;

    httpClient = new HttpClient
    {
        BaseAddress = new Uri(baseUrl),
        DefaultRequestHeaders = { { "Authorization", $"Bearer {apiKey}" } }
    };

    var clientLogger = loggerFactory.CreateLogger<OpenAiClient>();
    llmClient = new OpenAiClient(httpClient, model, clientLogger);
}

var tools = new ITool[] { new CalculatorTool(), new ReadFileTool(), new ListFilesTool(), new BashTool() };
var agent = new Agent(llmClient, tools, logger: agentLogger);

Console.WriteLine("SharpAgent - Type 'exit' to quit");
Console.WriteLine($"Provider: {provider} | Model: {modelName} | Log level: {logLevel}");
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
