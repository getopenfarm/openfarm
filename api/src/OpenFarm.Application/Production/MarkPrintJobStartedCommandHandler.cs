using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>FR-012's manual-brand path only — 409 if the job's printer has live integration (JobTelemetrySync owns that job's transitions instead) or the job isn't dispatched-but-not-started.</summary>
public sealed class MarkPrintJobStartedCommandHandler(
    IPrintJobRepository jobRepository, IPrinterRepository printerRepository, IProductionEventPublisher publisher)
    : ICommandHandler<MarkPrintJobStartedCommand, PrintJob>
{
    public async Task<PrintJob> HandleAsync(MarkPrintJobStartedCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(command.JobId, cancellationToken)
            ?? throw new PrintJobNotFoundException(command.JobId);

        if (job.Status != PrintJobStatus.Queued || job.PrinterId is not { } printerId)
        {
            throw new JobNotEligibleForRemoteControlException(job.Id);
        }

        var printer = await printerRepository.GetByIdAsync(printerId, cancellationToken)
            ?? throw new PrinterNotFoundException(printerId);

        if (PrinterModelCatalog.SupportsLiveIntegration(printer.Brand))
        {
            throw new JobNotEligibleForRemoteControlException(job.Id);
        }

        job.MarkStarted();
        printer.ObserveStatus("RUNNING");
        await jobRepository.SaveChangesAsync(cancellationToken);
        await publisher.PrintJobStatusChangedAsync(job.Id, job.Status, cancellationToken);

        return job;
    }
}
