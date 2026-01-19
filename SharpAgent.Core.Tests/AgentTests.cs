using NSubstitute;

namespace SharpAgent.Core.Tests;

public class AgentTests
{
    private static IAsyncEnumerable<LlmStreamEvent> CreateStreamEvents(string? content, IReadOnlyList<ToolCall>? toolCalls = null)
    {
        return CreateStreamEventsAsync(content, toolCalls);
    }

    private static async IAsyncEnumerable<LlmStreamEvent> CreateStreamEventsAsync(string? content, IReadOnlyList<ToolCall>? toolCalls)
    {
        if (!string.IsNullOrEmpty(content))
        {
            yield return new LlmTextDeltaEvent(content);
        }

        if (toolCalls is { Count: > 0 })
        {
            foreach (var tc in toolCalls)
            {
                yield return new LlmToolUseStartedEvent(tc.Id, tc.Name);
                yield return new LlmToolUseArgumentsDeltaEvent(tc.Id, tc.Arguments);
                yield return new LlmToolUseCompletedEvent(tc.Id);
            }
        }

        yield return new LlmMessageCompletedEvent(content, null, toolCalls);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Run_WithSimpleGoal_ReturnsLlmResponse()
    {
        var llm = Substitute.For<ILlmClient>();
        llm.StreamCompletionAsync(Arg.Any<IReadOnlyList<Message>>(), Arg.Any<IReadOnlyList<ITool>>(), Arg.Any<CancellationToken>())
            .Returns(CreateStreamEvents("Hello, I can help you with that!"));

        var agent = new Agent(llm, []);

        var result = await agent.RunAsync("Say hello");

        Assert.Equal("Hello, I can help you with that!", result);
    }

    [Fact]
    public async Task Run_WithToolCall_ExecutesToolAndContinues()
    {
        var llm = Substitute.For<ILlmClient>();
        var tool = Substitute.For<ITool>();
        tool.Name.Returns("calculator");
        tool.ExecuteAsync("2+2", Arg.Any<CancellationToken>()).Returns("4");

        var callCount = 0;
        llm.StreamCompletionAsync(Arg.Any<IReadOnlyList<Message>>(), Arg.Any<IReadOnlyList<ITool>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return callCount == 1
                    ? CreateStreamEvents(null, [new ToolCall("call_1", "calculator", "2+2")])
                    : CreateStreamEvents("The answer is 4.");
            });

        var agent = new Agent(llm, [tool]);

        var result = await agent.RunAsync("What is 2+2?");

        Assert.Equal("The answer is 4.", result);
        await tool.Received(1).ExecuteAsync("2+2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_ExceedsMaxIterations_ReturnsError()
    {
        var llm = Substitute.For<ILlmClient>();
        var tool = Substitute.For<ITool>();
        tool.Name.Returns("loop");
        tool.ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("looping");

        llm.StreamCompletionAsync(Arg.Any<IReadOnlyList<Message>>(), Arg.Any<IReadOnlyList<ITool>>(), Arg.Any<CancellationToken>())
            .Returns(CreateStreamEvents(null, [new ToolCall("call_1", "loop", "go")]));

        var agent = new Agent(llm, [tool], maxIterations: 3);

        var events = new List<Streaming.AgentStreamEvent>();
        await foreach (var evt in agent.RunStreamingAsync("Loop forever"))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is Streaming.AgentErrorEvent);
    }
}
