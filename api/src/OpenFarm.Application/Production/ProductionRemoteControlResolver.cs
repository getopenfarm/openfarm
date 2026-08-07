using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>The job/printer resolution + eligibility check shared by Pause/Resume/Cancel (FR-024) — one place for the "is this job in a state remote control applies to, and is its printer live-integrated" question every one of those three handlers otherwise repeats.</summary>
public sealed class ProductionRemoteControlResolver(
    IPrintJobRepository jobRepository, IPrinterRepository printerRepository, ResolvePrinterConnectionDetails detailsResolver)
{
    /// <param name="eligibleStatuses">The job statuses this action is valid from.</param>
    /// <returns><c>Printer</c>/<c>Details</c> are non-null only when the job's printer has live integration — a manual-brand job's caller applies the change directly instead.</returns>
    /// <exception cref="PrintJobNotFoundException"/>
    /// <exception cref="JobNotEligibleForRemoteControlException">Not in one of <paramref name="eligibleStatuses"/>, or not yet dispatched to a printer.</exception>
    /// <exception cref="PrinterNotFoundException"/>
    public async Task<(PrintJob Job, Printer? LiveIntegratedPrinter, PrinterConnectionDetails? Details)> ResolveAsync(
        Guid jobId, IReadOnlyCollection<PrintJobStatus> eligibleStatuses, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(jobId, cancellationToken) ?? throw new PrintJobNotFoundException(jobId);
        if (!eligibleStatuses.Contains(job.Status) || job.DispatchedPrinterId is not { } printerId)
        {
            throw new JobNotEligibleForRemoteControlException(job.Id);
        }

        var printer = await printerRepository.GetByIdAsync(printerId, cancellationToken) ?? throw new PrinterNotFoundException(printerId);
        if (!PrinterModelCatalog.SupportsLiveIntegration(printer.Brand))
        {
            return (job, null, null);
        }

        var details = await detailsResolver.ResolveAsync(printer, cancellationToken)
            ?? throw new InvalidOperationException($"Printer '{printer.Id}' has no connection secret.");
        return (job, printer, details);
    }
}
