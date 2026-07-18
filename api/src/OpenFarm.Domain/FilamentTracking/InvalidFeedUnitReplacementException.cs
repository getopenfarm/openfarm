namespace OpenFarm.Domain.FilamentTracking;

/// <summary>An Automatic Filament Reloader's <see cref="FeedUnit.ReplacesFeedUnitId"/> must reference an <see cref="FeedUnitKindCatalog.ExternalSpool"/>-kind unit on the same printer.</summary>
public sealed class InvalidFeedUnitReplacementException(Guid replacesFeedUnitId)
    : Exception($"Feed unit '{replacesFeedUnitId}' is not a valid External Spool replacement target.")
{
    public Guid ReplacesFeedUnitId { get; } = replacesFeedUnitId;
}
