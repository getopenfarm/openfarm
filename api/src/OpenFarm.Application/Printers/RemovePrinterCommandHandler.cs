using OpenFarm.Application.Common;

namespace OpenFarm.Application.Printers;

public sealed class RemovePrinterCommandHandler(
    IPrinterRepository printerRepository,
    IPrinterConnectionManager connectionManager) : ICommandHandler<RemovePrinterCommand, bool>
{
    public async Task<bool> HandleAsync(RemovePrinterCommand command, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(command.PrinterId, cancellationToken)
            ?? throw new PrinterNotFoundException(command.PrinterId);

        // FR-002/clarification: removal is never blocked by current print status.
        connectionManager.StopConnection(printer.Id);
        await printerRepository.RemoveAsync(printer, cancellationToken);

        return true;
    }
}
