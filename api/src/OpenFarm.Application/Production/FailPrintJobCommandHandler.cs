using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// Mirrors <see cref="CompletePrintJobCommandHandler"/>'s shape (FR-012/FR-013a/FR-014a) for the Failed outcome instead.
/// A manual-brand printer reports no progress, so nothing is deducted; the operator adjusts through manual spool usage.
/// </summary>
public sealed class FailPrintJobCommandHandler(
    IPrintJobRepository jobRepository,
    IPrinterRepository printerRepository,
    JobFilamentAutoDeduction filamentDeduction,
    IProductionEventPublisher publisher) : ICommandHandler<FailPrintJobCommand, PrintJob>
{
    public async Task<PrintJob> HandleAsync(FailPrintJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(command.JobId, cancellationToken)
            ?? throw new PrintJobNotFoundException(command.JobId);

        if (job.Status is PrintJobStatus.Printing or PrintJobStatus.Paused)
        {
            if (job.PrinterId is { } printerId
                && await printerRepository.GetByIdAsync(printerId, cancellationToken) is { } printer)
            {
                if (PrinterModelCatalog.SupportsLiveIntegration(printer.Brand))
                    throw new JobNotEligibleForRemoteControlException(job.Id);
                // See CompletePrintJobCommandHandler's own remarks — TransitionTo's manual-transition
                // guard doesn't permit this move, so this reuses ObserveStatus's always-legal path.
                printer.ObserveStatus("FAILED");
            }
            job.MarkFailed(reason: null);
        }
        else
        {
            throw new JobNotEligibleForRemoteControlException(job.Id);
        }

        await filamentDeduction.ApplyAsync(job, new JobProgress(job.Status), cancellationToken);
        await jobRepository.SaveChangesAsync(cancellationToken);
        await publisher.PrintJobStatusChangedAsync(job.Id, job.Status, cancellationToken);

        return job;
    }
}
