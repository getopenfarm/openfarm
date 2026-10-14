using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record RunLineBackfillLimitsInput(Guid LineId, int BackfillPrinterCap, int? BackfillCumulativeUnitCeiling);

public sealed record SetRunBackfillLimitsCommand(Guid ProductionRunId, IReadOnlyList<RunLineBackfillLimitsInput> Lines);

/// <summary>FR-006c/d for many lines of one run in a single save — unlimited lines only, else 409 (<see cref="RunLineNotEligibleForBackfillLimitsException"/>).</summary>
public sealed class SetRunBackfillLimitsCommandHandler(IProductionRunRepository runRepository, ProductionDispatchRequestService dispatchRequests)
    : ICommandHandler<SetRunBackfillLimitsCommand, ProductionRun>
{
    public async Task<ProductionRun> HandleAsync(SetRunBackfillLimitsCommand command, CancellationToken cancellationToken)
    {
        var run = await runRepository.GetByIdAsync(command.ProductionRunId, cancellationToken)
            ?? throw new ProductionRunNotFoundException(command.ProductionRunId);

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
        await dispatchRequests.RequestIdlePrintersAsync(cancellationToken);
        return run;
    }
}
