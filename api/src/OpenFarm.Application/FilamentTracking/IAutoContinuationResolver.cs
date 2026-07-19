using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// Resolves the kind-driven auto-continuation policy (FR-009–FR-011a) for a spool that just
/// depleted to exactly zero. US3 registers <see cref="NoOpAutoContinuationResolver"/>; US4 swaps in
/// <see cref="SpoolUsageRecordedHandler"/> — <see cref="RecordSpoolUsageCommandHandler"/> itself never
/// changes.
/// </summary>
public interface IAutoContinuationResolver
{
    Task<AutoContinuationResult> ResolveAsync(Spool depletedSpool, CancellationToken cancellationToken);
}

public sealed class NoOpAutoContinuationResolver : IAutoContinuationResolver
{
    public Task<AutoContinuationResult> ResolveAsync(Spool depletedSpool, CancellationToken cancellationToken) =>
        Task.FromResult(AutoContinuationResult.None);
}
