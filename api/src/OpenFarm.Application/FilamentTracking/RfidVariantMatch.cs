using OpenFarm.Application.Production;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>Which catalog colour an RFID spool most likely is, to prefill its registration. The operator confirms it.</summary>
public static class RfidVariantMatch
{
    /// <summary>
    /// Same material type, a filament of the tag's product line, and the clearly nearest colour by the same rule that pairs
    /// printer-started prints with trays (<see cref="ToolTrayMatcher"/>): tag and catalog colours rarely match exactly. Filaments
    /// named for the line ("PLA Matte", "Bambu PLA Matte") come first; a plainer name ("PLA") only when none is. Null when
    /// nothing, or more than one colour, fits.
    /// </summary>
    public static VariantStock? Find(IReadOnlyList<VariantStock> colors, AmsTray tray)
    {
        if (string.IsNullOrWhiteSpace(tray.SubBrand)) return null;
        var line = Words(tray.SubBrand);
        var named = colors.Where(c => line.IsSubsetOf(Words(c.Material))).ToList();
        var candidates = named.Count > 0 ? named : colors.Where(c => Words(c.Material).IsSubsetOf(line)).ToList();
        var match = ToolTrayMatcher.Match([(0, tray.MaterialType, tray.ColorHex)],
            candidates.Select((c, i) => new LoadedTray(i, c.MaterialType, c.ColorHex)).ToList(), []);
        return match.TryGetValue(0, out var index) ? candidates[index] : null;
    }

    /// <summary>
    /// Whether a tag's sub-brand ("PLA Basic") and a catalog filament's name are one product line: all the words of one are in
    /// the other, in any order and case. "Basic PLA" and "PLA" fit "PLA Basic"; "PLA Matte" does not. No sub-brand fits anything.
    /// </summary>
    public static bool SameLine(string? subBrand, string material) =>
        string.IsNullOrWhiteSpace(subBrand) || Words(subBrand).IsSubsetOf(Words(material)) || Words(material).IsSubsetOf(Words(subBrand));

    private static HashSet<string> Words(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
