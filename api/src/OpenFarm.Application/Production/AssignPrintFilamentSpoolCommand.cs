using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record AssignPrintFilamentSpoolCommand(Guid PrintId, Guid LineId, Guid SpoolId);

public sealed record AssignPrintFilamentSpoolResult(Guid PrintId, Guid LineId, Guid SpoolId, decimal GramsDeducted);

/// <summary>
/// Finishes a deduction automatic deduction skipped: binds the spool the operator names to the line, then re-runs the same
/// delta-based deduction from the progress stored when the print closed, so the result matches what an automatic one gives.
/// Only a line that skipped for want of a spool can be assigned; one that skipped for want of an estimate or progress cannot.
/// </summary>
public sealed class AssignPrintFilamentSpoolCommandHandler(
    IPrintRepository prints,
    ISpoolRepository spools,
    JobFilamentAutoDeduction deduction,
    IProductionEventPublisher publisher)
    : ICommandHandler<AssignPrintFilamentSpoolCommand, AssignPrintFilamentSpoolResult>
{
    public async Task<AssignPrintFilamentSpoolResult> HandleAsync(AssignPrintFilamentSpoolCommand command, CancellationToken cancellationToken)
    {
        var print = await prints.GetByIdAsync(command.PrintId, cancellationToken)
            ?? throw new PrintJobNotFoundException(command.PrintId);
        var line = print.Filaments.FirstOrDefault(f => f.Id == command.LineId)
            ?? throw new FilamentConsumptionSumMismatchException(null, null);
        var spool = await spools.GetByIdAsync(command.SpoolId, cancellationToken)
            ?? throw new SpoolNotFoundException(command.SpoolId);

        if (line.SkippedReason is FilamentSkipReason.NoEstimate or FilamentSkipReason.NoProgress)
            throw new FilamentUsageNotEstimableException(print.Id);
        if (line.SkippedReason is not (FilamentSkipReason.NoSpool or FilamentSkipReason.SpoolNotLoaded))
            throw new PrintFilamentNotAssignableException(print.Id, "nothing is left to deduct for this line.");
        if (spool.State != SpoolState.Using)
            throw new PrintFilamentNotAssignableException(print.Id, "the spool is not loaded.");

        print.BindLineSpool(line.Id, spool.Id);
        var progress = new JobProgress(print.Status, print.StopLayer, print.StopTotalLayers, print.StopPercent);
        await deduction.ApplyAsync(print, progress, cancellationToken, logSkipped: false);

        if (line.GramsConsumed is not { } grams) throw new FilamentUsageNotEstimableException(print.Id);

        await spools.SaveChangesAsync(cancellationToken);
        await publisher.NotificationsChangedAsync(cancellationToken);
        return new AssignPrintFilamentSpoolResult(print.Id, line.Id, spool.Id, grams);
    }
}
