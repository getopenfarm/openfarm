namespace OpenFarm.Domain.Parts;

public sealed record PartDeletionBlocker(string Code, string EntityType, Guid EntityId, string Name);

public sealed record PartDeletionEligibility(
    Guid PartId, string? Name, string Status,
    IReadOnlyList<PartDeletionBlocker> Blockers, int LegacyJobsLosingRecovery);

public sealed class PartDeletionBlockedException(PartDeletionEligibility eligibility)
    : Exception($"Part '{eligibility.Name}' cannot be removed. Referenced by: {string.Join(", ", eligibility.Blockers.Select(b => b.Name).Distinct())}.")
{
    public PartDeletionEligibility Eligibility { get; } = eligibility;
}
