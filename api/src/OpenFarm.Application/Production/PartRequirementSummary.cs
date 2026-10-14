namespace OpenFarm.Application.Production;

/// <param name="JobsNeedingRequeue">
/// Cancelled/Failed jobs on this requirement with RequeuedAsJobId == null (research.md R13) — a
/// plain filter, never a computed/stored aggregate. What the UI renders a shortfall banner and
/// "Requeue" action from.
/// </param>
public sealed record PartRequirementSummary(
    Guid Id,
    Guid PartId,
    string PartName,
    decimal? Quantity,
    int JobCount,
    int CompletedJobCount,
    IReadOnlyList<Guid> JobsNeedingRequeue, bool SourcePartAvailable = true, IReadOnlyList<Guid>? JobsBlockedForRequeue = null);
