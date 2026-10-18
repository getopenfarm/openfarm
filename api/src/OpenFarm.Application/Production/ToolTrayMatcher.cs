using System.Globalization;
using OpenFarm.Application.Printers;

namespace OpenFarm.Application.Production;

/// <summary>A tray the printer reports loaded: its <c>tray_now</c> id and the material and colour it reports for it.</summary>
public sealed record LoadedTray(int TrayId, string? MaterialType, string? ColorHex);

/// <summary>
/// Pairs the tools of a print the printer started itself with the AMS trays that fed them, from what each tool prints (material
/// and colour, read from the file) against what each tray reports. The printer never says which tray fed which tool, and trays
/// seen while printing can miss early ones, so observed trays only break ties. Nothing is guessed: a tool with no clearly best
/// tray stays unmatched, for the operator to assign.
/// </summary>
public static class ToolTrayMatcher
{
    // ponytail: a slicer colour and the colour the AMS reports for the same spool drift (#FB001C vs F72323), so match on RGB
    // distance. A tray must be this close, and this much closer than the runner-up; tune if real prints show misses or swaps.
    private const double MaxDistance = 80;
    private const double MinMargin = 40;

    /// <summary>Tool index to tray id for every tool that has one clearly best tray.</summary>
    public static IReadOnlyDictionary<int, int> Match(
        IEnumerable<(int ToolIndex, string? MaterialType, string? ColorHex)> tools,
        IReadOnlyList<LoadedTray> trays,
        IReadOnlyCollection<int> observedTrayIds)
    {
        var matched = new Dictionary<int, int>();
        foreach (var (toolIndex, material, color) in tools)
        {
            if (Rgb(color) is not { } want || string.IsNullOrEmpty(material)) continue;
            var ranked = trays
                .Where(t => string.Equals(t.MaterialType, material, StringComparison.OrdinalIgnoreCase))
                .Select(t => (t.TrayId, Distance: Rgb(t.ColorHex) is { } have ? Distance(want, have) : double.MaxValue))
                .Where(t => t.Distance <= MaxDistance)
                .OrderBy(t => t.Distance)
                .ToList();
            if (ranked.Count == 0) continue;

            var contenders = ranked.Where(t => t.Distance - ranked[0].Distance < MinMargin).ToList();
            if (contenders.Count == 1) matched[toolIndex] = contenders[0].TrayId;
            else if (contenders.Where(t => observedTrayIds.Contains(t.TrayId)).ToList() is [var seen]) matched[toolIndex] = seen.TrayId;
        }

        return matched;
    }

    private static (int R, int G, int B)? Rgb(string? colorHex) =>
        FilamentColor.Normalize(colorHex) is { } hex && int.TryParse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? (v >> 16 & 0xFF, v >> 8 & 0xFF, v & 0xFF)
            : null;

    private static double Distance((int R, int G, int B) a, (int R, int G, int B) b) =>
        Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));
}
