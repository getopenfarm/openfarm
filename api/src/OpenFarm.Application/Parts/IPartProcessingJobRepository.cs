using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public interface IPartProcessingJobRepository
{
    Task AddAsync(PartProcessingJob job, CancellationToken cancellationToken);

    Task<PartProcessingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Jobs left in an in-flight, non-terminal state (<c>Processing</c>, or <c>ReadyForReview</c> with
    /// its preview estimate still <c>Resolving</c>) — only reachable at process startup, since the
    /// in-memory work queue is never durable across restarts, so anything found here was orphaned by
    /// a previous process lifetime ending mid-job (crash, restart, deploy) rather than genuinely still
    /// being worked on.
    /// </summary>
    Task<IReadOnlyList<PartProcessingJob>> ListOrphanedInFlightAsync(CancellationToken cancellationToken);

    Task<bool> HasActiveReprocessAsync(Guid partId, CancellationToken cancellationToken) => Task.FromResult(false);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
