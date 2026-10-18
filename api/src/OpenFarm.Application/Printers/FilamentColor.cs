using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

/// <summary>
/// Derives a single representative filament color for a printer from its AMS state — used by the
/// fleet card's color dot. Prefers the loaded tray the printer marks <see cref="AmsTray.Active"/>
/// (the one it's currently feeding from); a real Bambu connector doesn't report active-tray
/// detection yet (deferred), so it falls back to the first non-empty tray across all units. Returns
/// null when nothing is loaded, letting the UI show its neutral placeholder.
/// </summary>
public static class FilamentColor
{
    public static string? FromAmsUnits(IReadOnlyList<AmsUnit> amsUnits)
    {
        var trays = amsUnits.SelectMany(unit => unit.Trays).Where(tray => !tray.Empty).ToList();
        var chosen = trays.FirstOrDefault(tray => tray.Active) ?? trays.FirstOrDefault();
        return Normalize(chosen?.ColorHex);
    }

    /// <summary>
    /// Normalizes the on-the-wire color to a CSS <c>#RRGGBB</c> string. Bambu reports 8-char RGBA
    /// (e.g. <c>000000FF</c>); the simulator already sends <c>#RRGGBB</c>. Both collapse to the first
    /// six hex digits with a single leading <c>#</c>.
    /// </summary>
    public static string? Normalize(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return null;
        }

        var digits = colorHex.TrimStart('#');
        if (digits.Length < 6)
        {
            return null;
        }

        return $"#{digits[..6]}";
    }
}
