using Microsoft.Extensions.Logging;
using SharpAgent.Core;
using SharpAgent.Core.Tools;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
    ?? throw new InvalidOperationException("Set OPENAI_API_KEY environment variable");
var baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1/";
if (!baseUrl.EndsWith('/')) baseUrl += "/";
var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
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
var clientLogger = loggerFactory.CreateLogger<OpenAiClient>();

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(baseUrl),
    DefaultRequestHeaders = { { "Authorization", $"Bearer {apiKey}" } }
};

var llmClient = new OpenAiClient(httpClient, model, clientLogger);
var tools = new ITool[] { new CalculatorTool(), new ReadFileTool(), new ListFilesTool() };
var agent = new Agent(llmClient, tools, logger: agentLogger);

Console.WriteLine("SharpAgent - Type 'exit' to quit");
Console.WriteLine($"Model: {model} | Log level: {logLevel}");
Console.WriteLine();

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();
    
    if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    try
    {
        var response = await agent.RunAsync(input);
        Console.WriteLine($"Agent: {response}");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}
