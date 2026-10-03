using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// FR-017/FR-024d: cancels every Queued, never-dispatched job belonging to the run; a job already
/// sent to a printer (dispatch-in-flight or Printing) is left alone and still records its eventual
/// outcome — this bulk action never sends a printer command (that remains FR-024's own, separate,
/// deliberate per-job action).
/// </summary>
public sealed class CancelProductionRunCommandHandler(
    IProductionRunRepository runRepository, IPrintJobRepository jobRepository, IProductionEventPublisher publisher)
    : ICommandHandler<CancelProductionRunCommand, ProductionRun>
{
    public async Task<ProductionRun> HandleAsync(CancelProductionRunCommand command, CancellationToken cancellationToken)
    {
        var run = await runRepository.GetByIdAsync(command.RunId, cancellationToken)
            ?? throw new ProductionRunNotFoundException(command.RunId);

        run.Cancel();

        var jobs = await jobRepository.ListByProductionRunIdAsync(run.Id, cancellationToken);
        var cancelledJobs = jobs.Where(j => j.Status == PrintJobStatus.Queued && j.DispatchedPrinterId is null).ToList();
        foreach (var job in cancelledJobs)
        {
            job.MarkCancelled();
        }

        await runRepository.SaveChangesAsync(cancellationToken);

        await publisher.ProductionRunStatusChangedAsync(run.OrganizationId, run.Id, run.Status, cancellationToken);
        foreach (var job in cancelledJobs)
        {
            await publisher.PrintJobStatusChangedAsync(job.OrganizationId, job.Id, job.Status, cancellationToken);
        }

        return run;
    }
}
