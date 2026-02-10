using Sharp.Core.Extensions;

namespace Sharp.Core.Tests;

public sealed class ExtensionLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public ExtensionLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharpagent-extension-loader-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void DiscoverAndLoad_WithConfiguredAssemblyPath_LoadsExtensionInstances()
    {
        var assemblyPath = typeof(LoadableTestExtension).Assembly.Location;

        var result = ExtensionLoader.DiscoverAndLoad(
            workingDirectory: _tempDir,
            agentDirectory: Path.Combine(_tempDir, "agent"),
            configuredPaths: [assemblyPath],
            includeDefaultDirectories: false);

        Assert.Contains(result.Extensions, extension => extension is LoadableTestExtension);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Severity == ExtensionDiagnosticSeverity.Error);
    }

    [Fact]
    public void DiscoverAndLoad_WithManifestDirectory_UsesManifestEntries()
    {
        var assemblyPath = typeof(LoadableTestExtension).Assembly.Location;
        var extensionPackageDir = Path.Combine(_tempDir, "package");
        Directory.CreateDirectory(extensionPackageDir);
        File.WriteAllText(
            Path.Combine(extensionPackageDir, "extension.json"),
            $$"""
            {
              "extensions": ["{{assemblyPath}}"]
            }
            """);

        var result = ExtensionLoader.DiscoverAndLoad(
            workingDirectory: _tempDir,
            agentDirectory: Path.Combine(_tempDir, "agent"),
            configuredPaths: [extensionPackageDir],
            includeDefaultDirectories: false);

        Assert.Contains(result.Extensions, extension => extension is LoadableTestExtension);
    }

    [Fact]
    public void DiscoverAndLoad_DefaultDirectories_IncludeGlobalAndProjectExtensionDirs()
    {
        var assemblyPath = typeof(LoadableTestExtension).Assembly.Location;
        var agentDir = Path.Combine(_tempDir, "agent");
        var workingDirectory = Path.Combine(_tempDir, "workspace");
        var globalExtensionDir = Path.Combine(agentDir, "extensions", "global-ext");
        var projectExtensionDir = Path.Combine(workingDirectory, ".sharp", "extensions", "project-ext");

        Directory.CreateDirectory(globalExtensionDir);
        Directory.CreateDirectory(projectExtensionDir);

        File.WriteAllText(
            Path.Combine(globalExtensionDir, "extension.json"),
            $$"""
            { "extensions": ["{{assemblyPath}}"] }
            """);
        File.WriteAllText(
            Path.Combine(projectExtensionDir, "extension.json"),
            $$"""
            { "extensions": ["{{assemblyPath}}"] }
            """);

        var result = ExtensionLoader.DiscoverAndLoad(
            workingDirectory: workingDirectory,
            agentDirectory: agentDir,
            includeDefaultDirectories: true);

        Assert.Contains(result.Extensions, extension => extension is LoadableTestExtension);
    }

    [Fact]
    public void DiscoverAndLoad_WithMissingConfiguredPath_ReportsDiagnostic()
    {
        var missingPath = Path.Combine(_tempDir, "missing", "plugin.dll");

        var result = ExtensionLoader.DiscoverAndLoad(
            workingDirectory: _tempDir,
            agentDirectory: Path.Combine(_tempDir, "agent"),
            configuredPaths: [missingPath],
            includeDefaultDirectories: false);

        Assert.Empty(result.Extensions);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == ExtensionDiagnosticSeverity.Warning
            && diagnostic.Message.Contains("does not exist", StringComparison.Ordinal));
    }
}

public sealed class LoadableTestExtension : IAgentExtension
{
    public string Name => "loadable-test-extension";

    public ValueTask InitializeAsync(IAgentExtensionApi api, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}
