using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>One spool joined to its catalog entry, vendor and (when loaded) Feed slot columns — everything <see cref="SpoolSummary"/> needs, without loading entities.</summary>
public sealed record SpoolRow(
    Guid Id,
    string? Label,
    Guid FilamentVariantId,
    string ColorName,
    string ColorHex,
    string Material,
    string VendorName,
    decimal SpoolWeightGrams,
    decimal RemainingWeightGrams,
    SpoolState State,
    Guid? FeedSlotId,
    bool HasUsageOverrun,
    SpoolSlotPlacement? Placement);

/// <summary>The columns <see cref="FeedUnitDisplay.SlotLabel(string, int, string?, int, string)"/> reads for a loaded spool.</summary>
public sealed record SpoolSlotPlacement(string UnitKind, int UnitIndex, string? UnitName, int SlotPosition, string PrinterBrand);

/// <summary>Rows for the requested page and the total number of rows matching the filters.</summary>
public sealed record SpoolRowPage(IReadOnlyList<SpoolRow> Rows, int TotalCount);

internal static class SpoolRowMapping
{
    public static SpoolSummary ToSummary(this SpoolRow row) => new(
        row.Id,
        row.Label,
        row.FilamentVariantId,
        row.ColorName,
        row.ColorHex,
        row.Material,
        row.VendorName,
        row.SpoolWeightGrams,
        row.RemainingWeightGrams,
        row.State,
        row.FeedSlotId,
        row.Placement is { } placement
            ? FeedUnitDisplay.SlotLabel(placement.UnitKind, placement.UnitIndex, placement.UnitName, placement.SlotPosition, placement.PrinterBrand)
            : null,
        row.HasUsageOverrun);
}
