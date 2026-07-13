using OpenFarm.Application.Common;

namespace OpenFarm.Application.Printers;

public sealed class GetPrinterDetailQueryHandler(IPrinterRepository printerRepository)
    : IQueryHandler<GetPrinterDetailQuery, PrinterDetail?>
{
    public async Task<PrinterDetail?> HandleAsync(GetPrinterDetailQuery query, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(query.PrinterId, cancellationToken);
        if (printer is null)
        {
            return null;
        }

        return new PrinterDetail(
            printer.Id,
            printer.Name,
            printer.Brand,
            printer.ModelCode,
            printer.IpAddress,
            printer.SerialNumber,
            printer.Status,
            printer.IsReachable,
            printer.NozzleTempC,
            printer.BedTempC,
            printer.PrintProgressPercent,
            printer.CurrentStageLabel,
            printer.AmsUnits);
    }
}
