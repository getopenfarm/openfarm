using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// FR-016c, the operator's sign-off and the only path to Closed: from Complete, or from Active once no finite work
/// remains (a run of unlimited lines only). Like Cancel, it stops the run's unsent jobs (<see cref="UnsentJobs"/>).
/// </summary>
public sealed class CloseProductionRunCommandHandler(
    IProductionRunRepository runRepository,
    IPrintJobRepository jobRepository,
    ProductionRunCompletionService completion,
    IProductionEventPublisher publisher,
    IActivityLogWriter? activityLogWriter = null)
    : ICommandHandler<CloseProductionRunCommand, ProductionRun>
{
    public async Task<ProductionRun> HandleAsync(CloseProductionRunCommand command, CancellationToken cancellationToken)
    {
        var run = await runRepository.GetByIdAsync(command.RunId, cancellationToken)
            ?? throw new ProductionRunNotFoundException(command.RunId);

        run.Close(finiteWorkRemains: run.Status == ProductionRunStatus.Active && await completion.FiniteWorkRemainsAsync(run, cancellationToken));
        var cancelledJobs = UnsentJobs.Cancel(await jobRepository.ListByProductionRunIdAsync(run.Id, cancellationToken));
        await runRepository.SaveChangesAsync(cancellationToken);

        await publisher.ProductionRunStatusChangedAsync(run.Id, run.Status, cancellationToken);
        await UnsentJobs.PublishAsync(cancelledJobs, command.ActorId, publisher, activityLogWriter, cancellationToken);
        return run;
    }
}
