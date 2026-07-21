using OpenFarm.Application.Common;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>
/// The Cancel action — valid from <c>Processing</c> or <c>ReadyForReview</c> (data-model.md). Actual
/// cancellation of an in-flight background slice is best-effort-by-omission here: no CTS is torn
/// down, but <c>PartProcessingBackgroundService</c>'s continuation drops the result once it sees the
/// job is <c>Discarded</c> (research.md §3b) — the same outcome, without a second cancellation seam.
/// </summary>
public sealed class DiscardPartProcessingJobCommandHandler(IPartProcessingJobRepository jobRepository)
    : ICommandHandler<DiscardPartProcessingJobCommand, bool>
{
    public async Task<bool> HandleAsync(DiscardPartProcessingJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(command.JobId, cancellationToken)
            ?? throw new PartProcessingJobNotFoundException(command.JobId);

        job.Discard();
        await jobRepository.SaveChangesAsync(cancellationToken);

        return true;
    }
}
