using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Printers;

/// <summary>
/// What the printer reports for one feed slot right now — a live overlay on the persisted <see cref="FeedSlot"/>.
/// </summary>
/// <param name="ExtruderIndex">The extruder this slot feeds, an index into the printer model's extruders.</param>
/// <param name="RfidId">The spool's own id read from its tag (Bambu's <c>tray_uuid</c>, the same on both of a spool's tags); null without one.</param>
/// <param name="RfidWeightGrams">The spool's full filament weight as its tag states it; null when the tag gives none.</param>
public sealed record FeedSlotReading(
    int SlotPosition,
    bool Empty,
    string? MaterialType,
    string? ColorHex,
    string? SubBrand,
    int? RemainPercent,
    bool Active,
    string? RfidId = null,
    int? RfidWeightGrams = null,
    int ExtruderIndex = 0);

/// <summary>
/// One feed unit as the printer reports it: a <see cref="FeedUnitKindCatalog.MultiMaterialUnit"/> (an AMS, a toolchanger's
/// heads) or an <see cref="FeedUnitKindCatalog.ExternalSpool"/> holder, whose unit index is the extruder it feeds.
/// </summary>
public sealed record FeedUnitReading(string Kind, int UnitIndex, IReadOnlyList<FeedSlotReading> Slots);

/// <summary>One extruder as the printer reports it: its nozzle and the feed slot feeding it now (null when none).</summary>
/// <param name="NozzleConfirmed">The nozzle was reported on the current connection, so production may rely on it.</param>
/// <param name="TempC">The extruder's own nozzle temperature, when the printer reports one per extruder.</param>
public sealed record ExtruderReading(int Index, decimal? NozzleDiameterMm = null, FeedSlotRef? ActiveSlot = null,
    bool NozzleConfirmed = false, decimal? TempC = null);
