namespace OpenFarm.Infrastructure.Persistence;

/// <summary>Search terms for ILIKE: trimmed, bounded and escaped with <c>\</c>. A bounded term count keeps a pasted paragraph from becoming a huge predicate.</summary>
public static class LikePatterns
{
    public const int MaxTerms = 8;
    public const int MaxTermLength = 64;

    /// <summary>Escaped terms, without wildcards.</summary>
    public static IEnumerable<string> Terms(string? search) =>
        (search ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(MaxTerms)
            .Select(term => (term.Length > MaxTermLength ? term[..MaxTermLength] : term)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal));

    /// <summary>One <c>%term%</c> pattern per term.</summary>
    public static IEnumerable<string> Contains(string? search) => Terms(search).Select(term => $"%{term}%");
}
