namespace OpenFarm.Domain.FilamentTracking;

/// <summary>A connector-derived <see cref="FeedUnit"/> (e.g. <see cref="FeedUnitKindCatalog.MultiMaterialUnit"/>) can never be manually removed (FR-005a).</summary>
public sealed class NotAnAutomaticFilamentReloaderException(Guid feedUnitId, string kind)
    : Exception($"Feed unit '{feedUnitId}' is a '{kind}', which cannot be manually removed.")
{
    public Guid FeedUnitId { get; } = feedUnitId;

    public string Kind { get; } = kind;
}
