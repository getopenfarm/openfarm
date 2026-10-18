using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>FR-016c — the only path to the terminal Closed state; throws InvalidRunCompletionException (409) unless Status == Complete.</summary>
public sealed class CloseProductionRunCommandHandler(IProductionRunRepository runRepository, IProductionEventPublisher publisher)
    : ICommandHandler<CloseProductionRunCommand, ProductionRun>
{
    public async Task<ProductionRun> HandleAsync(CloseProductionRunCommand command, CancellationToken cancellationToken)
    {
        var run = await runRepository.GetByIdAsync(command.RunId, cancellationToken)
            ?? throw new ProductionRunNotFoundException(command.RunId);

        run.Close();
        await runRepository.SaveChangesAsync(cancellationToken);
        await publisher.ProductionRunStatusChangedAsync(run.Id, run.Status, cancellationToken);

        return run;
    }
}
