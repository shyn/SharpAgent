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
}
