namespace OpenFarm.Domain.Printers;

/// <param name="RfidId">The spool's own id read from its tag (Bambu's <c>tray_uuid</c>, the same on both of a spool's tags); null without one.</param>
/// <param name="RfidWeightGrams">The spool's full filament weight as its tag states it; null when the tag gives none.</param>
public sealed record AmsTray(
    int SlotIndex,
    bool Empty,
    string? MaterialType,
    string? ColorHex,
    string? SubBrand,
    int? RemainPercent,
    bool Active,
    string? RfidId = null,
    int? RfidWeightGrams = null);

public sealed record AmsUnit(int UnitIndex, IReadOnlyList<AmsTray> Trays);
