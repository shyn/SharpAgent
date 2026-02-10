using System.Text;

namespace Sharp.AI;

internal static class ToolCallIdNormalizer
{
    public static string Normalize(string? rawId, int index)
    {
        var seed = string.IsNullOrWhiteSpace(rawId)
            ? $"call_{index}"
            : rawId.Trim();

        var builder = new StringBuilder(seed.Length);
        foreach (var ch in seed)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-')
                builder.Append(ch);
            else
                builder.Append('_');
        }

        if (builder.Length == 0)
            return $"call_{index}";

        return builder.Length <= 64
            ? builder.ToString()
            : builder.ToString(0, 64);
    }

    public static string NormalizeOpenAi(string? rawId, int index)
    {
        var seed = string.IsNullOrWhiteSpace(rawId)
            ? $"call_{index}"
            : rawId.Trim();

        var normalizedSeed = seed.Split('|', 2)[0];
        var builder = new StringBuilder(normalizedSeed.Length);
        foreach (var ch in normalizedSeed)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-')
                builder.Append(ch);
            else
                builder.Append('_');
        }

        var text = builder.ToString().TrimEnd('_');
        if (text.Length == 0)
            text = $"call_{index}";

        return text.Length <= 40
            ? text
            : text[..40];
    }

    public static string NormalizeMistral(string? rawId, int index)
    {
        var seed = string.IsNullOrWhiteSpace(rawId)
            ? $"call_{index}"
            : rawId.Trim();

        var filtered = new string(seed.Where(char.IsLetterOrDigit).ToArray());
        if (filtered.Length >= 9)
            return filtered[..9];

        var suffix = ShortHash(seed);
        var merged = filtered + suffix;
        if (merged.Length < 9)
            merged = merged.PadRight(9, '0');

        return merged[..9];
    }

    private static string ShortHash(string input)
    {
        var hash = 2166136261u;
        foreach (var ch in input)
        {
            hash ^= ch;
            hash *= 16777619;
        }

        return hash.ToString("x");
    }
}
