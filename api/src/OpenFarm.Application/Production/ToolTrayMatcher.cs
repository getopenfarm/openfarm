using System.Globalization;
using OpenFarm.Application.Printers;

namespace OpenFarm.Application.Production;

/// <summary>A tray the printer reports loaded: what identifies it (its feed slot) and the material and colour it reports for it.</summary>
public sealed record LoadedTray<TKey>(TKey Key, string? MaterialType, string? ColorHex);

/// <summary>
/// Pairs the tools of a print the printer started itself with the AMS trays that fed them, from what each tool prints (material
/// and colour, read from the file) against what each tray reports. The printer never says which tray fed which tool, and trays
/// seen while printing can miss early ones, so observed trays only break ties. Nothing is guessed: a tool with no clearly best
/// tray stays unmatched, for the operator to assign.
/// </summary>
public static class ToolTrayMatcher
{
    // ponytail: a slicer colour and the colour the AMS reports for the same spool drift (#FB001C vs F72323, ΔE 5.6), so match on
    // CIELAB ΔE76, not RGB: RGB puts pastels near white (pink #FFB7EE is 74 from white, ΔE 42). A tray must be this close, and
    // this much closer than the runner-up; tune if real prints show misses or swaps.
    public const double MaxDistance = 20;
    private const double MinMargin = 10;

    /// <summary>Tool index to tray key for every tool that has one clearly best tray.</summary>
    public static IReadOnlyDictionary<int, TKey> Match<TKey>(
        IEnumerable<(int ToolIndex, string? MaterialType, string? ColorHex)> tools,
        IReadOnlyList<LoadedTray<TKey>> trays,
        IReadOnlyCollection<TKey> observedTrays)
    {
        var matched = new Dictionary<int, TKey>();
        foreach (var (toolIndex, material, color) in tools)
        {
            if (Rgb(color) is not { } want || string.IsNullOrEmpty(material)) continue;
            var ranked = trays
                .Where(t => string.Equals(t.MaterialType, material, StringComparison.OrdinalIgnoreCase))
                .Select(t => (t.Key, Distance: Rgb(t.ColorHex) is { } have ? Distance(want, have) : double.MaxValue))
                .Where(t => t.Distance <= MaxDistance)
                .OrderBy(t => t.Distance)
                .ToList();
            if (ranked.Count == 0) continue;

            var contenders = ranked.Where(t => t.Distance - ranked[0].Distance < MinMargin).ToList();
            if (contenders.Count == 1) matched[toolIndex] = contenders[0].Key;
            else if (contenders.Where(t => observedTrays.Contains(t.Key)).ToList() is [var seen]) matched[toolIndex] = seen.Key;
        }

        return matched;
    }

    /// <summary>How far apart two colours are, as printers and the catalog write them (<c>RRGGBBAA</c>, <c>#RRGGBB</c>); null when either is unreadable.</summary>
    public static double? ColorDistance(string? a, string? b) => Rgb(a) is { } x && Rgb(b) is { } y ? Distance(x, y) : null;

    private static (int R, int G, int B)? Rgb(string? colorHex) =>
        FilamentColor.Normalize(colorHex) is { } hex && int.TryParse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? (v >> 16 & 0xFF, v >> 8 & 0xFF, v & 0xFF)
            : null;

    private static double Distance((int R, int G, int B) a, (int R, int G, int B) b)
    {
        var (l1, a1, b1) = Lab(a);
        var (l2, a2, b2) = Lab(b);
        return Math.Sqrt(Math.Pow(l1 - l2, 2) + Math.Pow(a1 - a2, 2) + Math.Pow(b1 - b2, 2));
    }

    /// <summary>sRGB to CIELAB (D65), so distances track how different two colours look.</summary>
    private static (double L, double A, double B) Lab((int R, int G, int B) color)
    {
        static double Linear(int channel)
        {
            var s = channel / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;

        double r = Linear(color.R), g = Linear(color.G), blue = Linear(color.B);
        var x = F((0.4124 * r + 0.3576 * g + 0.1805 * blue) / 0.95047);
        var y = F(0.2126 * r + 0.7152 * g + 0.0722 * blue);
        var z = F((0.0193 * r + 0.1192 * g + 0.9505 * blue) / 1.08883);
        return (116 * y - 16, 500 * (x - y), 200 * (y - z));
    }
}
