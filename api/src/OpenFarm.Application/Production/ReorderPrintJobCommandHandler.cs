using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// FR-008, the fractional-index move (research.md R3). New QueuePosition is the midpoint between
/// <c>AfterJobId</c>'s position and its current successor's (or an offset past whatever's currently
/// at that end). <see cref="PrintJob.Reorder"/> itself enforces the 409 (still Queued) guard.
/// </summary>
public sealed class ReorderPrintJobCommandHandler(IPrintJobRepository jobRepository)
    : ICommandHandler<ReorderPrintJobCommand, PrintJob>
{
    private const double Spacing = 1024;

    public async Task<PrintJob> HandleAsync(ReorderPrintJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(command.JobId, cancellationToken)
            ?? throw new PrintJobNotFoundException(command.JobId);

        double newPosition;
        if (command.AfterJobId is null)
        {
            var currentMin = await jobRepository.GetMinQueuedPositionAsync(cancellationToken);
            newPosition = currentMin is null ? Spacing : currentMin.Value - Spacing;
        }
        else
        {
            var afterJob = await jobRepository.GetByIdAsync(command.AfterJobId.Value, cancellationToken)
                ?? throw new PrintJobNotFoundException(command.AfterJobId.Value);

            var successor = await jobRepository.GetNextQueuedJobAsync(afterJob.QueuePosition, job.Id, cancellationToken);
            newPosition = successor is null
                ? afterJob.QueuePosition + Spacing
                : (afterJob.QueuePosition + successor.QueuePosition) / 2;
        }

        job.Reorder(newPosition);
        await jobRepository.SaveChangesAsync(cancellationToken);

        return job;
    }
}
