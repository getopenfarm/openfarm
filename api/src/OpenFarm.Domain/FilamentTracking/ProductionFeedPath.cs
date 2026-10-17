namespace OpenFarm.Domain.FilamentTracking;

/// <summary>Physical feed paths usable by production, shared by matching and profile resolution.</summary>
public static class ProductionFeedPath
{
    /// <summary>The printer's <c>tray_now</c> value for "the external spool is the filament in the extruder".</summary>
    public const int ExternalTrayId = 254;

    /// <summary>
    /// A printer with an AMS shares one filament inlet between the AMS and the external spool, and an external spool that
    /// is not already loaded into the extruder is never fed automatically. So exactly one of them is usable at a time:
    /// the external spool only while the printer reports it as the active feed (<paramref name="externalIsActiveFeed"/>),
    /// otherwise the AMS. A reloader replaces the external port and loads through the extruder, so it is always usable.
    /// </summary>
    public static IReadOnlySet<Guid> ActiveSlotIds(IReadOnlyList<FeedUnit> units, bool externalIsActiveFeed)
    {
        var active = units.Where(u => u.ArchivedAtUtc is null).ToList();
        var replaced = active.Where(u => u.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader
            && u.ReplacesFeedUnitId is not null).Select(u => u.ReplacesFeedUnitId!.Value).ToHashSet();
        var hasAms = HasAms(active);
        return active.Where(u => !replaced.Contains(u.Id) && (!hasAms || IsUsableWithAms(u.Kind, externalIsActiveFeed)))
            .SelectMany(u => u.FeedSlots.Where(s => s.DeactivatedAtUtc is null
                && (u.Kind != FeedUnitKindCatalog.AutomaticFilamentReloader || s.Id == u.FeedingSlotId)))
            .Select(s => s.Id).ToHashSet();
    }

    public static bool HasAms(IReadOnlyList<FeedUnit> units) =>
        units.Any(u => u.ArchivedAtUtc is null && u.Kind == FeedUnitKindCatalog.MultiMaterialUnit && u.FeedSlots.Any(s => s.DeactivatedAtUtc is null));

    private static bool IsUsableWithAms(string kind, bool externalIsActiveFeed) => kind switch
    {
        FeedUnitKindCatalog.MultiMaterialUnit => !externalIsActiveFeed,
        FeedUnitKindCatalog.ExternalSpool => externalIsActiveFeed,
        _ => true,
    };
}
