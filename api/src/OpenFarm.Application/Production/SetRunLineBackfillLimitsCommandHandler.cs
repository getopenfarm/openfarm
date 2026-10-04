using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>FR-006c/d — 409 (RunLineNotEligibleForBackfillLimitsException) if the line is not unlimited.</summary>
public sealed class SetRunLineBackfillLimitsCommandHandler(IProductionRunRepository runRepository, ProductionDispatchRequestService? dispatchRequests = null)
    : ICommandHandler<SetRunLineBackfillLimitsCommand, RunLine>
{
    public async Task<RunLine> HandleAsync(SetRunLineBackfillLimitsCommand command, CancellationToken cancellationToken)
    {
        var run = await runRepository.GetByRunLineIdAsync(command.RunLineId, cancellationToken);
        if (command.ProductionRunId is { } expected && run?.Id != expected) throw new RunLineNotFoundException(command.RunLineId);
        var line = run?.Lines.FirstOrDefault(l => l.Id == command.RunLineId)
            ?? throw new RunLineNotFoundException(command.RunLineId);

        if (line.Quantity is not null)
        {
            throw new RunLineNotEligibleForBackfillLimitsException(line.Id);
        }

        line.SetBackfillLimits(command.BackfillPrinterCap, command.BackfillCumulativeUnitCeiling);
        await runRepository.SaveChangesAsync(cancellationToken);

        if (dispatchRequests is not null) await dispatchRequests.RequestIdlePrintersAsync(cancellationToken);
        return line;
    }
}
