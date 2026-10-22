using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// Resolves the kind-driven auto-continuation policy (FR-009–FR-011a) for a spool that just depleted to exactly zero.
/// <see cref="SpoolUsageRecordedHandler"/> is the implementation; <see cref="RecordSpoolUsageCommandHandler"/> and the
/// automatic deduction call it and know nothing of the policy.
/// </summary>
public interface IAutoContinuationResolver
{
    Task<AutoContinuationResult> ResolveAsync(Spool depletedSpool, CancellationToken cancellationToken);
}
