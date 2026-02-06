using System.Text.Json;
using SharpAgent.Api.Models;
using SharpAgent.Core;
using SharpAgent.Core.Configuration;
using SharpAgent.Core.Streaming;
using SharpAgent.Core.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<ConfigurationService>(_ =>
{
    var svc = new ConfigurationService();
    svc.Load();
    return svc;
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://127.0.0.1:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

app.MapGet("/api/config", (ConfigurationService configService) =>
{
    var (providerId, modelId) = ConfigurationService.ParseModelString(configService.Config.DefaultModel);
    return new ConfigResponse
    {
        Provider = providerId,
        Model = configService.GetCurrentModelName(),
        HasApiKey = configService.HasApiKey()
    };
});

app.MapPost("/api/chat", async (HttpContext context, ConfigurationService configService, CancellationToken ct) =>
{
    var request = await context.Request.ReadFromJsonAsync<ChatRequest>(ct);
    if (request is null || string.IsNullOrWhiteSpace(request.Message))
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { error = "Message is required" }, ct);
        return;
    }

    // Determine model string from request or use default
    var modelString = !string.IsNullOrWhiteSpace(request.Model) 
        ? (request.Model.Contains('/') ? request.Model : $"{request.Provider ?? "openai"}/{request.Model}")
        : configService.Config.DefaultModel;

    var hasRequestApiKey = !string.IsNullOrWhiteSpace(request.ApiKey);
    if (!hasRequestApiKey && !configService.HasApiKey(modelString))
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { error = "API key not configured" }, ct);
        return;
    }

    var thinkingConfig = ParseThinkingLevel(request.ThinkingLevel);
    var (httpClient, llmClient) = CreateLlmClientFromRequest(request, configService, modelString, thinkingConfig);

    try
    {
        var tools = new ITool[]
        {
            new CalculatorTool(),
            new ReadFileTool(),
            new ListFilesTool(),
            new BashTool(),
            new EditFileTool(),
            new GlobTool()
        };

        var agent = await Agent.CreateAsync(llmClient, tools, new AgentOptions());

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        await foreach (var evt in agent.RunStreamingAsync(request.Message, ct))
        {
            var chatEvent = MapAgentEvent(evt);
            if (chatEvent is not null)
            {
                var json = JsonSerializer.Serialize(chatEvent, jsonOptions);
                await context.Response.WriteAsync($"data: {json}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }
        }

        await context.Response.WriteAsync("data: [DONE]\n\n", ct);
    }
    finally
    {
        httpClient.Dispose();
    }
});

app.Run();

static ThinkingConfig ParseThinkingLevel(string level) => level.ToLowerInvariant() switch
{
    "low" => new ThinkingConfig { Level = ThinkingLevel.Low },
    "medium" or "middle" => new ThinkingConfig { Level = ThinkingLevel.Middle },
    "high" => new ThinkingConfig { Level = ThinkingLevel.High },
    _ => ThinkingConfig.Disabled
};

static (HttpClient, ILlmClient) CreateLlmClientFromRequest(
    ChatRequest request, 
    ConfigurationService configService, 
    string modelString,
    ThinkingConfig thinkingConfig)
{
    var modelConfig = configService.GetModelConfig(modelString);
    if (modelConfig == null)
        throw new InvalidOperationException($"Model not found: {modelString}");

    var (provider, model) = modelConfig.Value;
    var apiKey = request.ApiKey ?? provider.ApiKey ?? "";
    var baseUrl = provider.BaseUrl;
    if (!baseUrl.EndsWith('/')) baseUrl += "/";

    var apiFormat = model.ApiFormats.FirstOrDefault();
    var maxTokens = model.MaxOutputTokens ?? 8192;
    
    if (apiFormat == ApiFormat.Anthropic)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            DefaultRequestHeaders =
            {
                { "x-api-key", apiKey },
                { "anthropic-version", "2023-06-01" }
            }
        };
        return (httpClient, new AnthropicClient(httpClient, model.Id, maxTokens, thinkingConfig));
    }
    else
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            DefaultRequestHeaders = { { "Authorization", $"Bearer {apiKey}" } }
        };
        return (httpClient, new OpenAiClient(httpClient, model.Id));
    }
}

static ChatEventDto? MapAgentEvent(AgentStreamEvent evt) => evt switch
{
    AgentStartedEvent e => new ChatEventDto { Type = "started", Data = new { goal = e.Goal } },
    AgentThinkingDeltaEvent e => new ChatEventDto { Type = "thinking_delta", Data = new ThinkingDeltaData { Thinking = e.Thinking } },
    AgentThinkingCompletedEvent e => new ChatEventDto { Type = "thinking_completed", Data = new ThinkingCompletedData { FullThinking = e.FullThinking } },
    AgentTextDeltaEvent e => new ChatEventDto { Type = "text_delta", Data = new TextDeltaData { Text = e.Text } },
    AgentToolUseStartedEvent e => new ChatEventDto { Type = "tool_use_started", Data = new ToolUseStartedData { Id = e.ToolCallId, Name = e.ToolName } },
    AgentToolUseArgumentsDeltaEvent e => new ChatEventDto { Type = "tool_use_args_delta", Data = new ToolUseArgumentsDeltaData { Id = e.ToolCallId, PartialJson = e.PartialJson } },
    AgentToolUseCompletedEvent e => new ChatEventDto { Type = "tool_use_completed", Data = new ToolUseCompletedData { Id = e.ToolCallId } },
    AgentToolCallStartedEvent e => new ChatEventDto { Type = "tool_call_started", Data = new ToolCallStartedData { Id = e.ToolCallId, Name = e.ToolName, Arguments = e.Arguments } },
    AgentToolCallCompletedEvent e => new ChatEventDto { Type = "tool_call_completed", Data = new ToolCallCompletedData { Id = e.ToolCallId, Result = e.Result, IsError = e.IsError } },
    AgentCompletedEvent e => new ChatEventDto { Type = "completed", Data = new CompletedData { FinalAnswer = e.FinalAnswer } },
    AgentErrorEvent e => new ChatEventDto { Type = "error", Data = new ErrorData { Message = e.Message } },
    _ => null
};

