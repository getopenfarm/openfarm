namespace OpenFarm.Domain.FilamentTracking;

/// <summary>
/// Whether and how a <see cref="FeedUnit"/> auto-continues onto another already-loaded spool when
/// one depletes — resolved from <see cref="FeedUnit.Kind"/> via <see cref="FeedUnitKindCatalog"/>,
/// never stored per-slot config (research.md §4).
/// </summary>
public enum ReloadPolicy
{
    None,
    MatchRequired,
    Unconditional,
}
