using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// FR-012/FR-013/FR-014a: the manual-brand completion path — transitions the job and moves its
/// printer to <see cref="PrinterStatus.AwaitingClear"/> in the same save. Also the sole way to attach
/// the filament-consumption body to an already-<c>Completed</c> live-integrated job (research.md R7 —
/// <c>IJobTelemetrySync</c> owns that transition itself, this only supplies the breakdown afterward).
/// Any other status 409s.
/// </summary>
public sealed class CompletePrintJobCommandHandler(
    IPrintJobRepository jobRepository,
    IPrinterRepository printerRepository,
    ISpoolRepository spoolRepository,
    IProductionEventPublisher publisher,
    ProductionRunCompletionService completionService) : ICommandHandler<CompletePrintJobCommand, PrintJob>
{
    public async Task<PrintJob> HandleAsync(CompletePrintJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(command.JobId, cancellationToken)
            ?? throw new PrintJobNotFoundException(command.JobId);

        if (job.Status is PrintJobStatus.Printing or PrintJobStatus.Paused)
        {
            job.MarkCompleted();

            if (job.DispatchedPrinterId is { } printerId
                && await printerRepository.GetByIdAsync(printerId, cancellationToken) is { } printer)
            {
                printer.TransitionTo(PrinterStatus.AwaitingClear);
            }
        }
        else if (job.Status != PrintJobStatus.Completed)
        {
            throw new JobNotEligibleForRemoteControlException(job.Id);
        }

        await PrintJobFilamentConsumptionRecorder.RecordAsync(job, command.FilamentConsumption, spoolRepository, cancellationToken);
        await jobRepository.SaveChangesAsync(cancellationToken);
        await publisher.PrintJobStatusChangedAsync(job.OrganizationId, job.Id, job.Status, cancellationToken);

        await completionService.TryCompleteRunForRequirementAsync(job.PartRequirementId, cancellationToken);

        return job;
    }
}
