using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Printers;

public sealed class RemovePrinterCommandHandler(
    IPrinterRepository printerRepository,
    IPrinterConnectionManager connectionManager,
    IPrinterCameraSnapshotStore snapshots,
    IPrintJobRepository jobs,
    IExternalPrintRepository externalPrints,
    JobFilamentAutoDeduction filamentDeduction,
    IProductionEventPublisher publisher,
    IProductionRunRepository? runs = null) : ICommandHandler<RemovePrinterCommand, bool>
{
    public async Task<bool> HandleAsync(RemovePrinterCommand command, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(command.PrinterId, cancellationToken)
            ?? throw new PrinterNotFoundException(command.PrinterId);

        // FR-002/clarification: removal is never blocked by current print status. A print still running there can't be
        // observed any more, so it ends now as Failed, its filament deducted to the last progress reported; a job only
        // dispatched goes back to the queue.
        connectionManager.StopConnection(printer.Id);
        var closed = new List<Print>();
        if (await jobs.GetActiveJobByDispatchedPrinterIdAsync(printer.Id, cancellationToken) is { } job)
        {
            if (job.Status == PrintJobStatus.Queued) job.RevertDispatch();
            else closed.Add(job);
        }

        // A pin to a printer that no longer exists would hold its jobs forever; they go to whichever printer matches.
        // After the revert above, so a pinned job only dispatched here is unsent again by now.
        foreach (var pinned in await jobs.ListQueuedPinnedToAsync(printer.Id, cancellationToken)) pinned.Unpin();
        if (runs is not null)
            foreach (var line in await runs.ListLinesPinnedToAsync(printer.Id, cancellationToken)) line.Unpin();

        if (await externalPrints.GetOpenByPrinterIdAsync(printer.Id, cancellationToken) is { } external)
        {
            if (external.Filaments.Count == 0)
            {
                // Its file never arrived: one line of unknown grams, so the operator sees what wasn't deducted.
                external.AttachFilaments(null);
                externalPrints.TrackNewFilaments(external.Filaments);
            }

            closed.Add(external);
        }

        foreach (var print in closed)
        {
            print.RecordStopProgress(printer.CurrentLayer, printer.TotalLayers, printer.PrintProgressPercent, printer.ActiveAlerts);
            print.MarkFailed(reason: "The printer was removed.");
            await filamentDeduction.ApplyAsync(print,
                new JobProgress(print.Status, print.StopLayer, print.StopTotalLayers, print.StopPercent), cancellationToken);
        }

        // One save: the prints' ends and the removal commit together.
        await printerRepository.RemoveAsync(printer, cancellationToken);
        await snapshots.DeleteAsync(printer.Id, cancellationToken);

        foreach (var print in closed) await publisher.PrintJobStatusChangedAsync(print.Id, print.Status, cancellationToken);
        if (closed.Count > 0) await publisher.NotificationsChangedAsync(cancellationToken);

        return true;
    }
}
