namespace OpenFarm.Domain.Production;

/// <summary>FR-006c/d — backfill printer cap/cumulative ceiling are only meaningful on an unlimited (Quantity == null) line.</summary>
public sealed class RunLineNotEligibleForBackfillLimitsException(Guid runLineId)
    : Exception($"Run line '{runLineId}' is not unlimited, so backfill limits don't apply to it.")
{
    public Guid RunLineId { get; } = runLineId;
}
