using Sharp.AI;

namespace Sharp.Cli.Tests;

public sealed class CliInvocationTests
{
    [Fact]
    public void Parse_EmptyArgs_DefaultsToRepl()
    {
        var invocation = CliInvocation.Parse([]);

        Assert.Equal("repl", invocation.Command);
        Assert.False(invocation.ShowHelp);
        Assert.Empty(invocation.Positionals);
        Assert.Equal(ThinkingLevel.Off, invocation.Options.ThinkingLevel);
        Assert.Equal(20, invocation.Options.MaxTurns);
        Assert.True(invocation.Options.EnableSkills);
        Assert.True(invocation.Options.DiscoverExtensions);
    }

    [Fact]
    public void Parse_RunCommand_WithOptionsAndPositionals_ParsesSuccessfully()
    {
        var invocation = CliInvocation.Parse(
        [
            "run",
            "--model", "openai/gpt-4o",
            "--thinking", "low",
            "--max-turns", "32",
            "--session", "session-42",
            "--no-skills",
            "--no-discover-extensions",
            "hello",
            "world"
        ]);

        Assert.Equal("run", invocation.Command);
        Assert.False(invocation.ShowHelp);
        Assert.Equal("openai/gpt-4o", invocation.Options.Model);
        Assert.Equal(ThinkingLevel.Low, invocation.Options.ThinkingLevel);
        Assert.Equal(32, invocation.Options.MaxTurns);
        Assert.Equal("session-42", invocation.Options.SessionId);
        Assert.False(invocation.Options.EnableSkills);
        Assert.False(invocation.Options.DiscoverExtensions);
        Assert.Equal(["hello", "world"], invocation.Positionals);
    }

    [Fact]
    public void Parse_HelpToken_ReturnsHelpInvocation()
    {
        var invocation = CliInvocation.Parse(["--help"]);

        Assert.Equal("help", invocation.Command);
        Assert.True(invocation.ShowHelp);
    }

    [Fact]
    public void Parse_UnknownOption_ThrowsUsageException()
    {
        var exception = Assert.Throws<CliUsageException>(() => CliInvocation.Parse(["run", "--unknown"]));

        Assert.Contains("--unknown", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ConfigInit_WithForce_ParsesSuccessfully()
    {
        var invocation = CliInvocation.Parse(["config", "init", "--force"]);

        Assert.Equal("config", invocation.Command);
        Assert.False(invocation.ShowHelp);
        Assert.True(invocation.Options.Force);
        Assert.Equal(["init"], invocation.Positionals);
    }

    [Fact]
    public void Parse_ConfigValidate_WithJson_ParsesSuccessfully()
    {
        var invocation = CliInvocation.Parse(["config", "validate", "--json"]);

        Assert.Equal("config", invocation.Command);
        Assert.False(invocation.ShowHelp);
        Assert.True(invocation.Options.JsonOutput);
        Assert.Equal(["validate"], invocation.Positionals);
    }

    [Fact]
    public void Parse_Repl_WithDebug_ParsesSuccessfully()
    {
        var invocation = CliInvocation.Parse(["repl", "--debug"]);

        Assert.Equal("repl", invocation.Command);
        Assert.False(invocation.ShowHelp);
        Assert.True(invocation.Options.Debug);
    }
}
