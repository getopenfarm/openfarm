using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// FR-024: a <c>Queued</c> job (never sent anywhere) cancels immediately, no printer involved. An
/// already-<c>Printing</c>/<c>Paused</c> job on a live-integrated printer sends
/// <see cref="IPrinterConnector.StopJobAsync"/> and stamps <see cref="PrintJob.RequestCancel"/> only —
/// <see cref="PrintJob.Status"/> stays untouched until telemetry confirms (FR-024c). The same job on a
/// manual-brand printer cancels directly. Any other (terminal) status 409s.
/// </summary>
public sealed class CancelPrintJobCommandHandler(
    IPrintJobRepository jobRepository,
    IPrinterRepository printerRepository,
    ResolvePrinterConnectionDetails detailsResolver,
    IPrinterConnectorFactory connectorFactory,
    IProductionEventPublisher publisher) : ICommandHandler<CancelPrintJobCommand, PrintJob>
{
    public async Task<PrintJob> HandleAsync(CancelPrintJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(command.JobId, cancellationToken)
            ?? throw new PrintJobNotFoundException(command.JobId);

        if (job.Status == PrintJobStatus.Queued && job.DispatchedAtUtc is null)
        {
            job.MarkCancelled();
            await jobRepository.SaveChangesAsync(cancellationToken);
            await publisher.PrintJobStatusChangedAsync(job.Id, job.Status, cancellationToken);
            return job;
        }

        if (job.Status is not (PrintJobStatus.Queued or PrintJobStatus.Printing or PrintJobStatus.Paused) || job.PrinterId is not { } printerId)
        {
            throw new JobNotEligibleForRemoteControlException(job.Id);
        }

        var printer = await printerRepository.GetByIdAsync(printerId, cancellationToken)
            ?? throw new PrinterNotFoundException(printerId);

        if (!PrinterModelCatalog.SupportsLiveIntegration(printer.Brand))
        {
            job.MarkCancelled();
            printer.ObserveStatus("FAILED");
            await jobRepository.SaveChangesAsync(cancellationToken);
            await publisher.PrintJobStatusChangedAsync(job.Id, job.Status, cancellationToken);
            return job;
        }

        var details = await detailsResolver.ResolveAsync(printer, cancellationToken)
            ?? throw new InvalidOperationException($"Printer '{printer.Id}' has no connection secret.");
        var connector = connectorFactory.CreateConnector(details);
        job.RequestCancel();
        await jobRepository.SaveChangesAsync(cancellationToken);
        await connector.StopJobAsync(details, cancellationToken);

        return job;
    }
}
