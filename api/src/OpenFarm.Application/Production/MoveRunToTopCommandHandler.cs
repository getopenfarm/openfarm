using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record MoveRunToTopCommand(Guid ProductionRunId);

/// <summary>
/// FR-008's manual order, one run at a time: the run's waiting (queued, unsent) jobs move above everything else as one
/// block, keeping their own order. The run-level replacement for a priority field (FR-008a: nothing else reorders).
/// </summary>
public sealed class MoveRunToTopCommandHandler(
    IProductionRunRepository runRepository, IPrintJobRepository jobRepository, ProductionDispatchRequestService? dispatchRequests = null)
    : ICommandHandler<MoveRunToTopCommand, ProductionRun>
{
    private const double Spacing = 1024;

    public async Task<ProductionRun> HandleAsync(MoveRunToTopCommand command, CancellationToken cancellationToken)
    {
        var run = await runRepository.GetByIdAsync(command.ProductionRunId, cancellationToken)
            ?? throw new ProductionRunNotFoundException(command.ProductionRunId);

        var waiting = (await jobRepository.ListByProductionRunIdAsync(run.Id, cancellationToken))
            .Where(j => j.Status == PrintJobStatus.Queued && j.DispatchedAtUtc is null)
            .OrderBy(j => j.QueuePosition).ThenBy(j => j.Id)
            .ToList();
        if (waiting.Count == 0)
        {
            return run;
        }

        var top = await jobRepository.GetMinQueuedPositionAsync(cancellationToken) ?? Spacing;
        for (var i = 0; i < waiting.Count; i++)
        {
            waiting[i].Reorder(top - (waiting.Count - i) * Spacing);
        }

        await jobRepository.SaveChangesAsync(cancellationToken);
        if (dispatchRequests is not null) await dispatchRequests.RequestIdlePrintersAsync(cancellationToken);
        return run;
    }
}
