using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record RequeueFailedJobsCommand(Guid ProductionRunId);

/// <summary>
/// Reprints every lost plate of a run's finite lines that still can be (has a snapshot), one requeue each. A plate
/// without a snapshot is left for a write-off. Returns how many were requeued.
/// </summary>
public sealed class RequeueFailedJobsCommandHandler(
    IProductionRunRepository runRepository,
    IPartRequirementRepository requirementRepository,
    IPrintJobRepository jobRepository,
    ICommandHandler<RequeuePrintJobCommand, PrintJob> requeue)
    : ICommandHandler<RequeueFailedJobsCommand, int>
{
    public async Task<int> HandleAsync(RequeueFailedJobsCommand command, CancellationToken cancellationToken)
    {
        var run = await runRepository.GetByIdAsync(command.ProductionRunId, cancellationToken)
            ?? throw new ProductionRunNotFoundException(command.ProductionRunId);
        if (!run.IsOpen)
        {
            throw new ProductionRunNotOpenException(run.Id);
        }

        var finite = (await requirementRepository.ListByProductionRunIdAsync(run.Id, cancellationToken))
            .Where(r => r.Quantity is not null).Select(r => r.Id).ToHashSet();
        var lost = (await jobRepository.ListByProductionRunIdAsync(run.Id, cancellationToken))
            .Where(j => finite.Contains(j.PartRequirementId) && j.IsUnresolvedLoss && j.ProductionSnapshotJson is not null
                && !(j.Status == PrintJobStatus.Completed && j.ObjectExecution is { Observed: false }))
            .ToList();
        foreach (var job in lost)
        {
            await requeue.HandleAsync(new RequeuePrintJobCommand(job.Id), cancellationToken);
        }

        return lost.Count;
    }
}
