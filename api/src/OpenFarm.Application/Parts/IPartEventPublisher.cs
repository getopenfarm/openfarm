using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>
/// Application-defined contract; <c>OpenFarm.Api</c> implements it via <c>IHubContext&lt;FarmHub&gt;</c>
/// (contracts/part-library-api.md's SignalR section), mirroring <c>IPrinterEventPublisher</c> exactly.
/// </summary>
public interface IPartEventPublisher
{
    /// <summary>Pushed on every <see cref="PartProcessingJob"/> status transition (research.md §3/§3b).</summary>
    Task PartProcessingJobStatusChangedAsync(Guid organizationId, PartProcessingJob job, CancellationToken cancellationToken);

    Task PartUpsertedAsync(Guid organizationId, Guid partId, CancellationToken cancellationToken);

    /// <summary>research.md §3b — the trailing continuation resolved an early-confirmed part's estimate.</summary>
    Task PartEstimateResolvedAsync(Guid organizationId, Guid partId, CancellationToken cancellationToken);

    /// <summary>research.md §3b — the trailing continuation instead failed; <see cref="EstimateStatus.Unavailable"/> is permanent.</summary>
    Task PartEstimateUnavailableAsync(Guid organizationId, Guid partId, CancellationToken cancellationToken);

    Task PartDeletedAsync(Guid organizationId, Guid partId, CancellationToken cancellationToken);
}
