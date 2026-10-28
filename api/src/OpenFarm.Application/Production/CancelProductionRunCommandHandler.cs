using OpenFarm.Application.Common;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>FR-017: cancels the run and its unsent jobs (<see cref="UnsentJobs"/>); a job already on a printer finishes.</summary>
public sealed class CancelProductionRunCommandHandler(
    IProductionRunRepository runRepository, IPrintJobRepository jobRepository, IProductionEventPublisher publisher, IActivityLogWriter? activityLogWriter = null)
    : ICommandHandler<CancelProductionRunCommand, ProductionRun>
{
    public async Task<ProductionRun> HandleAsync(CancelProductionRunCommand command, CancellationToken cancellationToken)
    {
        var run = await runRepository.GetByIdAsync(command.RunId, cancellationToken)
            ?? throw new ProductionRunNotFoundException(command.RunId);

        run.Cancel();
        var cancelledJobs = UnsentJobs.Cancel(await jobRepository.ListByProductionRunIdAsync(run.Id, cancellationToken));
        await runRepository.SaveChangesAsync(cancellationToken);

        await publisher.ProductionRunStatusChangedAsync(run.Id, run.Status, cancellationToken);
        await UnsentJobs.PublishAsync(cancelledJobs, command.ActorId, publisher, activityLogWriter, cancellationToken);
        return run;
    }
}
