using OpenFarm.Application.Common;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Application.Printers;

public sealed class GetPrinterDetailQueryHandler(
    IPrinterRepository printerRepository, IMachineProfileRepository machineProfileRepository)
    : IQueryHandler<GetPrinterDetailQuery, PrinterDetail?>
{
    public async Task<PrinterDetail?> HandleAsync(GetPrinterDetailQuery query, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(query.PrinterId, cancellationToken);
        if (printer is null)
        {
            return null;
        }

        // The link can dangle if its Machine Profile was deleted independently — treated as
        // unlinked for display purposes rather than surfacing as an error here.
        var machineProfileName = printer.MachineProfileId is { } profileId
            ? (await machineProfileRepository.GetByIdAsync(profileId, cancellationToken))?.Name
            : null;

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
            printer.RemainingTimeMinutes,
            printer.CurrentJobName,
            printer.AmsUnits,
            printer.MachineProfileId,
            machineProfileName);
    }
}
