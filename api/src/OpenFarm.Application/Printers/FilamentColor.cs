using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

/// <summary>
/// Derives a single representative filament color for a printer from its multi-material units — used
/// by the fleet card's color dot. Prefers the loaded slot the printer marks
/// <see cref="FeedSlotReading.Active"/> (the one it's currently feeding from), else the first
/// non-empty slot across all units. Returns null when nothing is loaded, letting the UI show its
/// neutral placeholder.
/// </summary>
public static class FilamentColor
{
    public static string? FromFeedReadings(IReadOnlyList<FeedUnitReading> readings)
    {
        var slots = readings.Where(unit => unit.Kind == FeedUnitKindCatalog.MultiMaterialUnit)
            .SelectMany(unit => unit.Slots).Where(slot => !slot.Empty).ToList();
        var chosen = slots.FirstOrDefault(slot => slot.Active) ?? slots.FirstOrDefault();
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
