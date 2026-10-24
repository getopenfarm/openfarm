using OpenFarm.Domain.Printers;

namespace OpenFarm.Domain.FilamentTracking;

/// <summary>Physical feed paths usable by production, shared by matching and profile resolution.</summary>
public static class ProductionFeedPath
{
    /// <summary>
    /// An extruder with an AMS shares one filament inlet between the AMS and its external spool, and an external spool that
    /// is not already loaded into the extruder is never fed automatically. So exactly one of them is usable at a time: the
    /// external spool only while the printer reports it as the slot feeding that extruder, otherwise the AMS. A reloader
    /// replaces the external port and loads through the extruder, so it is always usable.
    /// </summary>
    public static IReadOnlySet<Guid> ActiveSlotIds(IReadOnlyList<FeedUnit> units, IReadOnlyList<ExtruderReading> extruders)
    {
        var active = units.Where(u => u.ArchivedAtUtc is null).ToList();
        var replaced = active.Where(u => u.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader
            && u.ReplacesFeedUnitId is not null).Select(u => u.ReplacesFeedUnitId!.Value).ToHashSet();
        var amsExtruders = active.Where(u => u.Kind == FeedUnitKindCatalog.MultiMaterialUnit)
            .SelectMany(u => u.FeedSlots.Where(s => s.DeactivatedAtUtc is null)).Select(s => s.ExtruderIndex).ToHashSet();
        return active.Where(u => !replaced.Contains(u.Id))
            .SelectMany(u => u.FeedSlots
                .Where(s => s.DeactivatedAtUtc is null
                    && (u.Kind != FeedUnitKindCatalog.AutomaticFilamentReloader || s.Id == u.FeedingSlotId)
                    && (!amsExtruders.Contains(s.ExtruderIndex) || IsUsableWithAms(u.Kind, ExternalIsActiveFeed(extruders, s.ExtruderIndex)))))
            .Select(s => s.Id).ToHashSet();
    }

    public static bool HasAms(IReadOnlyList<FeedUnit> units) =>
        units.Any(u => u.ArchivedAtUtc is null && u.Kind == FeedUnitKindCatalog.MultiMaterialUnit && u.FeedSlots.Any(s => s.DeactivatedAtUtc is null));

    /// <summary>Whether the printer reports the external spool (not a multi-material unit) as the slot feeding this extruder.</summary>
    public static bool ExternalIsActiveFeed(IReadOnlyList<ExtruderReading> extruders, int extruderIndex) =>
        extruders.FirstOrDefault(e => e.Index == extruderIndex)?.ActiveSlot is { IsMultiMaterialUnit: false };

    private static bool IsUsableWithAms(string kind, bool externalIsActiveFeed) => kind switch
    {
        FeedUnitKindCatalog.MultiMaterialUnit => !externalIsActiveFeed,
        FeedUnitKindCatalog.ExternalSpool => externalIsActiveFeed,
        _ => true,
    };
}
