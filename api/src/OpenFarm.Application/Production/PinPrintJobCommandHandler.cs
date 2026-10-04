using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>FR-007 — 400 (PinnedJobRequiresPrinterException) if PrinterId is missing, 404 on missing job/printer, 409 if not Queued (PrintJob.Pin's own EnsureQueued guard).</summary>
public sealed class PinPrintJobCommandHandler(IPrintJobRepository jobRepository, IPrinterRepository printerRepository, ProductionDispatchRequestService? dispatchRequests = null)
    : ICommandHandler<PinPrintJobCommand, PrintJob>
{
    public async Task<PrintJob> HandleAsync(PinPrintJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(command.JobId, cancellationToken)
            ?? throw new PrintJobNotFoundException(command.JobId);

        if (command.PrinterId is { } printerId
            && await printerRepository.GetByIdAsync(printerId, cancellationToken) is null)
        {
            throw new PrinterNotFoundException(printerId);
        }

        job.Pin(command.PrinterId);
        await jobRepository.SaveChangesAsync(cancellationToken);

        if (dispatchRequests is not null) await dispatchRequests.RequestIdlePrintersAsync(cancellationToken);
        return job;
    }
}
