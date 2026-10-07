namespace OpenFarm.Domain.FilamentTracking;

/// <summary>Physical feed paths usable by production, shared by matching and profile resolution.</summary>
public static class ProductionFeedPath
{
    public static IReadOnlySet<Guid> ActiveSlotIds(IReadOnlyList<FeedUnit> units)
    {
        var active = units.Where(u => u.ArchivedAtUtc is null).ToList();
        var replaced = active.Where(u => u.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader
            && u.ReplacesFeedUnitId is not null).Select(u => u.ReplacesFeedUnitId!.Value).ToHashSet();
        return active.Where(u => !replaced.Contains(u.Id))
            .SelectMany(u => u.FeedSlots.Where(s => s.DeactivatedAtUtc is null
                && (u.Kind != FeedUnitKindCatalog.AutomaticFilamentReloader || s.Id == u.FeedingSlotId)))
            .Select(s => s.Id).ToHashSet();
    }
}
