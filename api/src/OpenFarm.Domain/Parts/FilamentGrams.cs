namespace OpenFarm.Domain.Parts;

/// <summary>One filament's estimated grams for a sliced file (per plate set), purge included — keyed like a requirement.</summary>
public sealed record FilamentGrams(string? MaterialType, string? ColorHex, decimal Grams)
{
    /// <summary>
    /// Splits a file's per-filament grams over its grouped requirements: a lone requirement takes everything (the STL
    /// "any material" wildcard has no colour to match), otherwise each takes its (material, colour) matches.
    /// All null when there is nothing to split, or when some grams match no requirement — a partial split would make the
    /// colours sum to less than the file's total, so callers fall back to the part total instead.
    /// </summary>
    public static decimal?[] Distribute(IReadOnlyList<(string? MaterialType, string? ColorHex)> keys, IReadOnlyList<FilamentGrams>? grams)
    {
        var result = new decimal?[keys.Count];
        if (grams is not { Count: > 0 } || keys.Count == 0) return result;
        if (keys.Count == 1)
        {
            result[0] = grams.Sum(g => g.Grams);
            return result;
        }

        var matched = 0m;
        for (var i = 0; i < keys.Count; i++)
        {
            var sum = grams.Where(g => string.Equals(g.MaterialType, keys[i].MaterialType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(g.ColorHex, keys[i].ColorHex, StringComparison.OrdinalIgnoreCase)).Sum(g => g.Grams);
            if (sum > 0) result[i] = sum;
            matched += sum;
        }

        return matched == grams.Sum(g => g.Grams) ? result : new decimal?[keys.Count];
    }
}
