using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Production;

public sealed class ProductionDispatchRequestService(IPrinterRepository printers, IProductionDispatcher dispatcher)
{
    public async Task RequestIdlePrintersAsync(CancellationToken cancellationToken)
    {
        foreach (var printer in await printers.ListAsync(cancellationToken))
            if (printer.Status == PrinterStatus.Idle) dispatcher.RequestDispatchAttempt(printer.Id);
    }
}
