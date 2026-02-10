namespace Sharp.Core.Tests.TestDoubles;

public static class FixturePaths
{
    public static string Root { get; } = ResolveFixtureRoot();

    public static string Get(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(Root, normalized);
    }

    private static string ResolveFixtureRoot()
    {
        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        while (cursor != null)
        {
            var candidate = Path.Combine(cursor.FullName, "fixtures");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "large-session.jsonl")))
                return candidate;

            cursor = cursor.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Sharp.Core.Tests/fixtures.");
    }
}
