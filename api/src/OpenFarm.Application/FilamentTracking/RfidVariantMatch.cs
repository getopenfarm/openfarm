using OpenFarm.Application.Production;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>Which catalog colour an RFID spool most likely is, to prefill its registration. The operator confirms it.</summary>
public static class RfidVariantMatch
{
    /// <summary>
    /// Same material type, a filament named for the tag's sub-brand ("PLA Matte"), and the clearly nearest colour by the same
    /// rule that pairs printer-started prints with trays (<see cref="ToolTrayMatcher"/>): tag and catalog colours rarely match
    /// exactly. Null when nothing, or more than one colour, fits.
    /// </summary>
    public static VariantStock? Find(IReadOnlyList<VariantStock> colors, AmsTray tray)
    {
        if (string.IsNullOrWhiteSpace(tray.SubBrand)) return null;
        var candidates = colors.Where(c => c.Material.Contains(tray.SubBrand, StringComparison.OrdinalIgnoreCase)).ToList();
        var match = ToolTrayMatcher.Match([(0, tray.MaterialType, tray.ColorHex)],
            candidates.Select((c, i) => new LoadedTray(i, c.MaterialType, c.ColorHex)).ToList(), []);
        return match.TryGetValue(0, out var index) ? candidates[index] : null;
    }
}
