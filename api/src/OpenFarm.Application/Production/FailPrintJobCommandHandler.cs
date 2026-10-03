using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>Mirrors <see cref="CompletePrintJobCommandHandler"/>'s shape (FR-012/FR-013a/FR-014a) for the Failed outcome instead.</summary>
public sealed class FailPrintJobCommandHandler(
    IPrintJobRepository jobRepository,
    IPrinterRepository printerRepository,
    ISpoolRepository spoolRepository,
    IProductionEventPublisher publisher) : ICommandHandler<FailPrintJobCommand, PrintJob>
{
    public async Task<PrintJob> HandleAsync(FailPrintJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(command.JobId, cancellationToken)
            ?? throw new PrintJobNotFoundException(command.JobId);

        if (job.Status is PrintJobStatus.Printing or PrintJobStatus.Paused)
        {
            job.MarkFailed(reason: null);

            if (job.DispatchedPrinterId is { } printerId
                && await printerRepository.GetByIdAsync(printerId, cancellationToken) is { } printer)
            {
                // See CompletePrintJobCommandHandler's own remarks — TransitionTo's manual-transition
                // guard doesn't permit this move, so this reuses ObserveStatus's always-legal path.
                printer.ObserveStatus("FAILED");
            }
        }
        else if (job.Status != PrintJobStatus.Failed)
        {
            throw new JobNotEligibleForRemoteControlException(job.Id);
        }

        await PrintJobFilamentConsumptionRecorder.RecordAsync(job, command.FilamentConsumption, spoolRepository, cancellationToken);
        await jobRepository.SaveChangesAsync(cancellationToken);
        await publisher.PrintJobStatusChangedAsync(job.OrganizationId, job.Id, job.Status, cancellationToken);

        return job;
    }
}
