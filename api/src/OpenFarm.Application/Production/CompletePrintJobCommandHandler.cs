using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// FR-012/FR-013/FR-014a: the manual-brand completion path — transitions the job and moves its
/// printer to <see cref="PrinterStatus.AwaitingClear"/> in the same save and deducting the job's full
/// filament estimate. A live-integrated job is completed and deducted by <c>IJobTelemetrySync</c> instead.
/// Any other status 409s.
/// </summary>
public sealed class CompletePrintJobCommandHandler(
    IPrintJobRepository jobRepository,
    IPrinterRepository printerRepository,
    JobFilamentAutoDeduction filamentDeduction,
    IProductionEventPublisher publisher,
    ProductionRunCompletionService completionService) : ICommandHandler<CompletePrintJobCommand, PrintJob>
{
    public async Task<PrintJob> HandleAsync(CompletePrintJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(command.JobId, cancellationToken)
            ?? throw new PrintJobNotFoundException(command.JobId);

        if (job.Status is PrintJobStatus.Printing or PrintJobStatus.Paused)
        {
            if (job.DispatchedPrinterId is { } printerId
                && await printerRepository.GetByIdAsync(printerId, cancellationToken) is { } printer)
            {
                if (PrinterModelCatalog.SupportsLiveIntegration(printer.Brand))
                    throw new JobNotEligibleForRemoteControlException(job.Id);
                // Printer.TransitionTo's manual-transition guard only permits AwaitingClear -> Idle
                // ("mark ready") — a manual-brand job has no gcode telemetry stream to reconcile
                // against, so this reuses ObserveStatus's lenient, always-legal reconciliation path
                // with the same synthetic terminal state a live-integrated printer would report.
                printer.ObserveStatus("FINISH");
            }
            job.MarkCompleted();
        }
        else
        {
            throw new JobNotEligibleForRemoteControlException(job.Id);
        }

        await filamentDeduction.ApplyAsync(job, new JobProgress(job.Status), cancellationToken);
        await jobRepository.SaveChangesAsync(cancellationToken);
        await publisher.PrintJobStatusChangedAsync(job.OrganizationId, job.Id, job.Status, cancellationToken);

        await completionService.TryCompleteRunForRequirementAsync(job.PartRequirementId, cancellationToken);

        return job;
    }
}
