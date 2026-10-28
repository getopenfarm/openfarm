using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

/// <summary>
/// FR-003/SC-004: the only manual status transition this phase supports — AwaitingClear -&gt; Idle.
/// Any other current status throws <see cref="InvalidPrinterStatusTransitionException"/> (mapped to
/// 409 by the API's global exception handler). Pushes the same
/// PrinterStatusChanged event a connector-reported reconciliation would, since the frontend has no
/// polling fallback (plan.md's Constitution Check). 014 FR-014b: requests a dispatch attempt for this
/// printer immediately, since this is the sole path back to Idle and a job may already be waiting.
/// </summary>
public sealed class MarkPrinterReadyCommandHandler(
    IPrinterRepository printerRepository,
    IPrinterEventPublisher printerEventPublisher,
    IProductionDispatcher productionDispatcher) : ICommandHandler<MarkPrinterReadyCommand, Printer>
{
    public async Task<Printer> HandleAsync(MarkPrinterReadyCommand command, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(command.PrinterId, cancellationToken)
            ?? throw new PrinterNotFoundException(command.PrinterId);

        PrinterStatusTransitions.EnsureValidManualTransition(printer.Status, PrinterStatus.Idle);
        printer.TransitionTo(PrinterStatus.Idle);
        await printerRepository.SaveChangesAsync(cancellationToken);

        await printerEventPublisher.PrinterStatusChangedAsync(
            printer.Id,
            printer.Status,
            printer.IsReachable,
            cancellationToken);

        productionDispatcher.RequestDispatchAttempt(printer.Id);

        return printer;
    }
}
