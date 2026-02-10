using System.Reflection;
using System.Text.Json;

namespace Sharp.Core.Extensions;

public sealed record ExtensionLoadResult(
    IReadOnlyList<IAgentExtension> Extensions,
    IReadOnlyList<ExtensionDiagnostic> Diagnostics);

public static class ExtensionLoader
{
    private const string ProjectConfigDirectoryName = ".sharp";
    private const string ExtensionsDirectoryName = "extensions";
    private const string ManifestFileName = "extension.json";
    private const string DefaultEntryAssemblyFileName = "index.dll";

    public static ExtensionLoadResult DiscoverAndLoad(
        string workingDirectory,
        string agentDirectory,
        IReadOnlyList<string>? configuredPaths = null,
        bool includeDefaultDirectories = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentDirectory);

        var extensions = new List<IAgentExtension>();
        var diagnostics = new List<ExtensionDiagnostic>();
        var discoveredAssemblyPaths = new List<string>();
        var seenAssemblyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddAssemblyPath(string assemblyPath)
        {
            var fullPath = Path.GetFullPath(assemblyPath);
            if (seenAssemblyPaths.Add(fullPath))
                discoveredAssemblyPaths.Add(fullPath);
        }

        if (includeDefaultDirectories)
        {
            var globalExtensionsDirectory = Path.Combine(agentDirectory, ExtensionsDirectoryName);
            foreach (var assemblyPath in DiscoverAssembliesInDirectory(globalExtensionsDirectory, diagnostics))
                AddAssemblyPath(assemblyPath);

            var projectExtensionsDirectory = Path.Combine(
                workingDirectory,
                ProjectConfigDirectoryName,
                ExtensionsDirectoryName);
            foreach (var assemblyPath in DiscoverAssembliesInDirectory(projectExtensionsDirectory, diagnostics))
                AddAssemblyPath(assemblyPath);
        }

        if (configuredPaths is { Count: > 0 })
        {
            foreach (var configuredPath in configuredPaths)
            {
                if (string.IsNullOrWhiteSpace(configuredPath))
                    continue;

                var resolvedPath = Path.IsPathRooted(configuredPath)
                    ? configuredPath
                    : Path.GetFullPath(Path.Combine(workingDirectory, configuredPath));

                if (Directory.Exists(resolvedPath))
                {
                    var entries = ResolveDirectoryEntries(resolvedPath, diagnostics);
                    if (entries.Count > 0)
                    {
                        foreach (var assemblyPath in entries)
                            AddAssemblyPath(assemblyPath);
                        continue;
                    }

                    foreach (var assemblyPath in DiscoverAssembliesInDirectory(resolvedPath, diagnostics))
                        AddAssemblyPath(assemblyPath);
                    continue;
                }

                if (File.Exists(resolvedPath) && IsAssemblyFileName(resolvedPath))
                {
                    AddAssemblyPath(resolvedPath);
                    continue;
                }

                diagnostics.Add(new ExtensionDiagnostic(
                    ExtensionDiagnosticSeverity.Warning,
                    $"Configured extension path does not exist or is not a .dll file: {resolvedPath}"));
            }
        }

        foreach (var assemblyPath in discoveredAssemblyPaths)
        {
            foreach (var extension in LoadExtensionsFromAssembly(assemblyPath, diagnostics))
                extensions.Add(extension);
        }

        return new ExtensionLoadResult(extensions, diagnostics);
    }

    private static IReadOnlyList<string> DiscoverAssembliesInDirectory(
        string directoryPath,
        List<ExtensionDiagnostic> diagnostics)
    {
        if (!Directory.Exists(directoryPath))
            return [];

        var assemblies = new List<string>();

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directoryPath))
            {
                if (File.Exists(entry))
                {
                    if (IsAssemblyFileName(entry))
                        assemblies.Add(Path.GetFullPath(entry));
                    continue;
                }

                if (Directory.Exists(entry))
                {
                    foreach (var resolved in ResolveDirectoryEntries(entry, diagnostics))
                        assemblies.Add(resolved);
                }
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ExtensionDiagnostic(
                ExtensionDiagnosticSeverity.Warning,
                $"Failed to discover extensions in directory '{directoryPath}': {ex.Message}"));
        }

        return assemblies;
    }

    private static IReadOnlyList<string> ResolveDirectoryEntries(
        string directoryPath,
        List<ExtensionDiagnostic> diagnostics)
    {
        if (!Directory.Exists(directoryPath))
            return [];

        var manifestPath = Path.Combine(directoryPath, ManifestFileName);
        if (File.Exists(manifestPath))
        {
            var manifestEntries = ReadManifestEntries(manifestPath, diagnostics);
            if (manifestEntries.Count > 0)
                return manifestEntries;
        }

        var defaultEntryPath = Path.Combine(directoryPath, DefaultEntryAssemblyFileName);
        return File.Exists(defaultEntryPath)
            ? [Path.GetFullPath(defaultEntryPath)]
            : [];
    }

    private static IReadOnlyList<string> ReadManifestEntries(
        string manifestPath,
        List<ExtensionDiagnostic> diagnostics)
    {
        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<ExtensionManifest>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (manifest?.Extensions is not { Count: > 0 })
                return [];

            var baseDirectory = Path.GetDirectoryName(manifestPath) ?? ".";
            var entries = new List<string>();

            foreach (var extensionPath in manifest.Extensions)
            {
                if (string.IsNullOrWhiteSpace(extensionPath))
                    continue;

                var resolvedPath = Path.IsPathRooted(extensionPath)
                    ? extensionPath
                    : Path.GetFullPath(Path.Combine(baseDirectory, extensionPath));

                if (!File.Exists(resolvedPath) || !IsAssemblyFileName(resolvedPath))
                {
                    diagnostics.Add(new ExtensionDiagnostic(
                        ExtensionDiagnosticSeverity.Warning,
                        $"Skipping extension manifest entry that is not an existing .dll file: {resolvedPath}"));
                    continue;
                }

                entries.Add(resolvedPath);
            }

            return entries;
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ExtensionDiagnostic(
                ExtensionDiagnosticSeverity.Warning,
                $"Failed to parse extension manifest '{manifestPath}': {ex.Message}"));
            return [];
        }
    }

    private static IEnumerable<IAgentExtension> LoadExtensionsFromAssembly(
        string assemblyPath,
        List<ExtensionDiagnostic> diagnostics)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(assemblyPath);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ExtensionDiagnostic(
                ExtensionDiagnosticSeverity.Error,
                $"Failed to load extension assembly '{assemblyPath}': {ex.Message}"));
            yield break;
        }

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(type => type != null).Cast<Type>().ToArray();

            var loaderMessages = ex.LoaderExceptions
                .Where(loaderException => loaderException != null)
                .Select(loaderException => loaderException!.Message)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (loaderMessages.Count > 0)
            {
                diagnostics.Add(new ExtensionDiagnostic(
                    ExtensionDiagnosticSeverity.Error,
                    $"Partial type load failure in '{assemblyPath}': {string.Join("; ", loaderMessages)}"));
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ExtensionDiagnostic(
                ExtensionDiagnosticSeverity.Error,
                $"Failed to inspect extension assembly '{assemblyPath}': {ex.Message}"));
            yield break;
        }

        var extensionTypes = types
            .Where(type =>
                typeof(IAgentExtension).IsAssignableFrom(type)
                && type is { IsClass: true, IsAbstract: false, IsPublic: true })
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

        if (extensionTypes.Count == 0)
        {
            diagnostics.Add(new ExtensionDiagnostic(
                ExtensionDiagnosticSeverity.Warning,
                $"No public IAgentExtension implementations found in '{assemblyPath}'."));
            yield break;
        }

        foreach (var extensionType in extensionTypes)
        {
            if (extensionType.GetConstructor(Type.EmptyTypes) == null)
            {
                diagnostics.Add(new ExtensionDiagnostic(
                    ExtensionDiagnosticSeverity.Warning,
                    $"Skipping extension type '{extensionType.FullName}' because it does not have a parameterless constructor."));
                continue;
            }

            IAgentExtension? instance;
            try
            {
                instance = Activator.CreateInstance(extensionType) as IAgentExtension;
            }
            catch (Exception ex)
            {
                diagnostics.Add(new ExtensionDiagnostic(
                    ExtensionDiagnosticSeverity.Error,
                    $"Failed to instantiate extension type '{extensionType.FullName}': {ex.Message}"));
                continue;
            }

            if (instance == null)
            {
                diagnostics.Add(new ExtensionDiagnostic(
                    ExtensionDiagnosticSeverity.Error,
                    $"Failed to instantiate extension type '{extensionType.FullName}': instance is null."));
                continue;
            }

            yield return instance;
        }
    }

    private static bool IsAssemblyFileName(string path)
        => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private sealed class ExtensionManifest
    {
        public List<string>? Extensions { get; set; }
    }
}
