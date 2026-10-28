using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record RunLineBackfillLimitsInput(Guid LineId, int BackfillPrinterCap, int? BackfillCumulativeUnitCeiling);

/// <summary>An open run's details and its unlimited lines' backfill limits (FR-006c/d), saved as one draft.</summary>
public sealed record UpdateProductionRunCommand(
    Guid ProductionRunId, string? Name, DateOnly? DueDate, string? Notes, IReadOnlyList<RunLineBackfillLimitsInput> Lines);

/// <summary>409 for a closed or cancelled run, or for limits on a line with a quantity (<see cref="RunLineNotEligibleForBackfillLimitsException"/>).</summary>
public sealed class UpdateProductionRunCommandHandler(
    IProductionRunRepository runRepository, ProductionDispatchRequestService dispatchRequests, IProductionEventPublisher publisher)
    : ICommandHandler<UpdateProductionRunCommand, ProductionRun>
{
    public async Task<ProductionRun> HandleAsync(UpdateProductionRunCommand command, CancellationToken cancellationToken)
    {
        var run = await runRepository.GetByIdAsync(command.ProductionRunId, cancellationToken)
            ?? throw new ProductionRunNotFoundException(command.ProductionRunId);

        run.UpdateDetails(command.Name, command.DueDate, command.Notes);
        foreach (var input in command.Lines)
        {
            var line = run.Lines.FirstOrDefault(l => l.Id == input.LineId) ?? throw new RunLineNotFoundException(input.LineId);
            if (line.Quantity is not null)
            {
                throw new RunLineNotEligibleForBackfillLimitsException(line.Id);
            }

            line.SetBackfillLimits(input.BackfillPrinterCap, input.BackfillCumulativeUnitCeiling);
        }

        await runRepository.SaveChangesAsync(cancellationToken);
        await publisher.ProductionRunStatusChangedAsync(run.Id, run.Status, cancellationToken);
        await dispatchRequests.RequestIdlePrintersAsync(cancellationToken);
        return run;
    }
}
