namespace Sharp.Core.Tools;

internal static class PathResolver
{
    public static string ResolveRead(string workingDirectory, string path)
        => Resolve(workingDirectory, path, enforceWorkspaceBoundary: false);

    public static string ResolveWrite(
        string workingDirectory,
        string path,
        bool allowOutsideWorkspace)
        => Resolve(workingDirectory, path, enforceWorkspaceBoundary: !allowOutsideWorkspace);

    private static string Resolve(
        string workingDirectory,
        string path,
        bool enforceWorkspaceBoundary)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required", nameof(path));

        var resolved = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(workingDirectory, path));

        if (enforceWorkspaceBoundary)
        {
            var workspaceRoot = EnsureTrailingSeparator(Path.GetFullPath(workingDirectory));
            var candidate = Path.GetFullPath(resolved);
            var insideWorkspace = candidate.StartsWith(workspaceRoot, StringComparison.Ordinal)
                                  || string.Equals(candidate, workspaceRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);

            if (!insideWorkspace)
                throw new InvalidOperationException($"Path '{candidate}' is outside workspace '{workingDirectory}'");
        }

        return resolved;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            return path;

        return path + Path.DirectorySeparatorChar;
    }
}
