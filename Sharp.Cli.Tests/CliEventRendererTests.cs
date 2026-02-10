using Sharp.AI;
using Sharp.Core;

namespace Sharp.Cli.Tests;

public sealed class CliEventRendererTests
{
    [Fact]
    public void Render_ToolLifecycle_PrintsToolCallArgumentsAndResult()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var renderer = new CliEventRenderer(stdout, stderr);

        renderer.Render(new AgentStartedEvent("list files"));
        renderer.Render(new AgentToolUseStartedEvent("call_1", "bash"));
        renderer.Render(new AgentToolUseArgumentsDeltaEvent("call_1", "{\"command\":\"ls\"}"));
        renderer.Render(new AgentToolUseCompletedEvent("call_1"));
        renderer.Render(new AgentToolExecutionStartedEvent("call_1", "bash", "{\"command\":\"ls\"}"));
        renderer.Render(new AgentToolExecutionUpdatedEvent("call_1", "bash", ToolInvocationResult.Text("a.txt")));
        renderer.Render(new AgentToolExecutionCompletedEvent(
            "call_1",
            "bash",
            ToolInvocationResult.Text("a.txt", details: new { exitCode = 0 })));
        renderer.Render(new AgentTextDeltaEvent("done"));
        renderer.Render(new AgentCompletedEvent(LlmMessage.AssistantText("done")));

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();

        Assert.Contains("done", stdoutText, StringComparison.Ordinal);
        Assert.Contains("[turn:start]", stderrText, StringComparison.Ordinal);
        Assert.Contains("[tool:call:start] bash (call_1)", stderrText, StringComparison.Ordinal);
        Assert.Contains("[tool:call:ready] bash (call_1)", stderrText, StringComparison.Ordinal);
        Assert.Contains("\"command\": \"ls\"", stderrText, StringComparison.Ordinal);
        Assert.Contains("[tool:exec:start] bash (call_1)", stderrText, StringComparison.Ordinal);
        Assert.Contains("[tool:exec:update] bash (call_1)", stderrText, StringComparison.Ordinal);
        Assert.Contains("[tool:exec:ok] bash (call_1)", stderrText, StringComparison.Ordinal);
        Assert.Contains("[result:end]", stderrText, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(stdoutText, "done"));
    }

    [Fact]
    public void Render_ThinkingLifecycle_PrintsThinkingStages()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var renderer = new CliEventRenderer(stdout, stderr);

        renderer.Render(new AgentStartedEvent("solve task"));
        renderer.Render(new AgentThinkingStartedEvent());
        renderer.Render(new AgentThinkingDeltaEvent("collect facts"));
        renderer.Render(new AgentThinkingCompletedEvent("collect facts"));
        renderer.Render(new AgentTextDeltaEvent("answer"));
        renderer.Render(new AgentTurnCompletedEvent(LlmMessage.AssistantText("answer"), []));
        renderer.Render(new AgentCompletedEvent(LlmMessage.AssistantText("answer")));

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();

        Assert.Contains("answer", stdoutText, StringComparison.Ordinal);
        Assert.Contains("[thinking:start]", stderrText, StringComparison.Ordinal);
        Assert.Contains("[thinking:end] collect facts", stderrText, StringComparison.Ordinal);
        Assert.Contains("[turn:end] tool_results=0", stderrText, StringComparison.Ordinal);
        Assert.Contains("[result:end]", stderrText, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_CompletedWithoutTextDelta_FallsBackToAssistantMessageText()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var renderer = new CliEventRenderer(stdout, stderr);

        renderer.Render(new AgentStartedEvent("reply"));
        renderer.Render(new AgentCompletedEvent(LlmMessage.AssistantText("final answer")));

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();

        Assert.Contains("final answer", stdoutText, StringComparison.Ordinal);
        Assert.Contains("[result:end]", stderrText, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var index = text.IndexOf(value, start, StringComparison.Ordinal);
            if (index < 0)
                return count;

            count++;
            start = index + value.Length;
        }
    }
}
