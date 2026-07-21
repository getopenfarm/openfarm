namespace OpenFarm.Domain.Parts;

/// <summary>
/// Tolerant "newest" ordering over version strings within a single engine (research.md §3). Parses
/// a dotted numeric release (<c>2.2.0</c>, <c>2.5.59.13</c>, Bambu's <c>01.10.00.00</c>) into a
/// variable-length list of integer components, compares component-wise with the shorter list
/// zero-padded, and only falls back to a lexical comparison of the raw strings when either side
/// carries a non-numeric suffix (an RC/beta tag orders before the same release without one) or the
/// numeric components are otherwise equal. Never assumes anything about a version from a different
/// engine — comparison is meaningful only between two versions of the same engine.
/// </summary>
public sealed class SlicerVersionComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        var (xComponents, xSuffix) = Parse(x);
        var (yComponents, ySuffix) = Parse(y);

        var length = Math.Max(xComponents.Count, yComponents.Count);
        for (var i = 0; i < length; i++)
        {
            var xValue = i < xComponents.Count ? xComponents[i] : 0;
            var yValue = i < yComponents.Count ? yComponents[i] : 0;
            var comparison = xValue.CompareTo(yValue);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        // Equal numeric components: a non-numeric suffix (e.g. "-rc1") orders before the clean
        // release with no suffix; otherwise fall back to an ordinal string comparison as the tiebreak.
        if (xSuffix.Length == 0 && ySuffix.Length > 0)
        {
            return 1;
        }

        if (xSuffix.Length > 0 && ySuffix.Length == 0)
        {
            return -1;
        }

        return string.CompareOrdinal(x, y);
    }

    private static (List<int> Components, string Suffix) Parse(string version)
    {
        var suffixIndex = version.IndexOfAny(['-', '+']);
        var numericPart = suffixIndex >= 0 ? version[..suffixIndex] : version;
        var suffix = suffixIndex >= 0 ? version[suffixIndex..] : string.Empty;

        var components = numericPart
            .Split('.')
            .Select(part => int.TryParse(part, out var value) ? value : 0)
            .ToList();

        return (components, suffix);
    }
}
