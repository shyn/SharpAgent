using System.Text.Json;

namespace Sharp.Cli.Tests;

public sealed class SharpCliAppConfigCommandTests
{
    [Fact]
    public async Task RunAsync_ConfigInit_CreatesConfigFile()
    {
        var configPath = NewTempFilePath();
        try
        {
            var exitCode = await SharpCliApp.RunAsync(["config", "init", "--config", configPath], CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(configPath));
        }
        finally
        {
            TryDelete(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigInit_ExistingWithoutForce_ReturnsUsageError()
    {
        var configPath = NewTempFilePath();
        try
        {
            File.WriteAllText(configPath, "{}");

            var exitCode = await SharpCliApp.RunAsync(["config", "init", "--config", configPath], CancellationToken.None);

            Assert.Equal(2, exitCode);
        }
        finally
        {
            TryDelete(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigInit_WithForce_OverwritesExistingFile()
    {
        var configPath = NewTempFilePath();
        try
        {
            File.WriteAllText(configPath, """{ "defaultModel": "invalid/model" }""");

            var exitCode = await SharpCliApp.RunAsync(
                ["config", "init", "--config", configPath, "--force"],
                CancellationToken.None);

            Assert.Equal(0, exitCode);

            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<Sharp.Core.Configuration.AgentConfig>(json);
            Assert.NotNull(config);
            Assert.Equal("openai/gpt-4o-mini", config.DefaultModel);
        }
        finally
        {
            TryDelete(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigValidate_ValidConfig_ReturnsSuccess()
    {
        var configPath = NewTempFilePath();
        try
        {
            File.WriteAllText(configPath,
                """
                {
                  "defaultModel": "openai/gpt-4o-mini",
                  "providers": [
                    {
                      "id": "openai",
                      "api": "openai-completions",
                      "apiKey": "test-key",
                      "baseUrl": "https://api.openai.com/v1/",
                      "models": [
                        {
                          "id": "gpt-4o-mini"
                        }
                      ]
                    }
                  ]
                }
                """);

            var exitCode = await SharpCliApp.RunAsync(
                ["config", "validate", "--config", configPath],
                CancellationToken.None);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            TryDelete(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ConfigValidate_MissingFile_ReturnsFailure()
    {
        var configPath = NewTempFilePath();
        TryDelete(configPath);

        var exitCode = await SharpCliApp.RunAsync(
            ["config", "validate", "--config", configPath],
            CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_ConfigValidate_MissingFile_WithJson_ReturnsFailure()
    {
        var configPath = NewTempFilePath();
        TryDelete(configPath);

        var exitCode = await SharpCliApp.RunAsync(
            ["config", "validate", "--config", configPath, "--json"],
            CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_ConfigInit_WithJson_ReturnsUsageError()
    {
        var configPath = NewTempFilePath();
        try
        {
            var exitCode = await SharpCliApp.RunAsync(
                ["config", "init", "--config", configPath, "--json"],
                CancellationToken.None);

            Assert.Equal(2, exitCode);
        }
        finally
        {
            TryDelete(configPath);
        }
    }

    private static string NewTempFilePath()
        => Path.Combine(Path.GetTempPath(), $"sharp-cli-config-{Guid.NewGuid():N}.json");

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
