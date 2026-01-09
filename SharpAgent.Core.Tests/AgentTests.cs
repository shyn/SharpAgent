using NSubstitute;

namespace SharpAgent.Core.Tests;

public class AgentTests
{
    [Fact]
    public async Task Run_WithSimpleGoal_ReturnsLlmResponse()
    {
        var llm = Substitute.For<ILlmClient>();
        llm.GetCompletionAsync(Arg.Any<IReadOnlyList<Message>>(), Arg.Any<IReadOnlyList<ITool>>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse("Hello, I can help you with that!"));

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

        llm.GetCompletionAsync(Arg.Any<IReadOnlyList<Message>>(), Arg.Any<IReadOnlyList<ITool>>(), Arg.Any<CancellationToken>())
            .Returns(
                new LlmResponse(null, [new ToolCall("call_1", "calculator", "2+2")]),
                new LlmResponse("The answer is 4."));

        var agent = new Agent(llm, [tool]);

        var result = await agent.RunAsync("What is 2+2?");

        Assert.Equal("The answer is 4.", result);
        await tool.Received(1).ExecuteAsync("2+2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_ExceedsMaxIterations_Throws()
    {
        var llm = Substitute.For<ILlmClient>();
        var tool = Substitute.For<ITool>();
        tool.Name.Returns("loop");
        tool.ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("looping");

        llm.GetCompletionAsync(Arg.Any<IReadOnlyList<Message>>(), Arg.Any<IReadOnlyList<ITool>>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(null, [new ToolCall("call_1", "loop", "go")]));

        var agent = new Agent(llm, [tool], maxIterations: 3);

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.RunAsync("Loop forever"));
    }
}
