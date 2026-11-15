using OpenFarm.Application.Common;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

/// <summary>
/// An operator hold: Idle -&gt; AwaitingClear, shown as "Not ready". Any other current status throws
/// <see cref="InvalidPrinterStatusTransitionException"/> (409): a Printing printer becomes Not ready by itself when
/// its print ends, and an AwaitingClear one already is. Dispatch needs no change: every gate asks for Idle.
/// Pushes the same PrinterStatusChanged event as <see cref="MarkPrinterReadyCommandHandler"/>, which has no
/// dispatch request, since a hold only removes the printer from the pool.
/// </summary>
public sealed class MarkPrinterNotReadyCommandHandler(
    IPrinterRepository printerRepository,
    IPrinterEventPublisher printerEventPublisher) : ICommandHandler<MarkPrinterNotReadyCommand, Printer>
{
    public async Task<Printer> HandleAsync(MarkPrinterNotReadyCommand command, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(command.PrinterId, cancellationToken)
            ?? throw new PrinterNotFoundException(command.PrinterId);

        printer.TransitionTo(PrinterStatus.AwaitingClear);
        await printerRepository.SaveChangesAsync(cancellationToken);

        await printerEventPublisher.PrinterStatusChangedAsync(
            printer.Id,
            printer.Status,
            printer.IsReachable,
            cancellationToken);

        return printer;
    }
}
