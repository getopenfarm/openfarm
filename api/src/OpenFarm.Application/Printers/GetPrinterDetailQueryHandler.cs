using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Printers;

public sealed class GetPrinterDetailQueryHandler(IPrinterRepository printerRepository, IPrintRepository printRepository)
    : IQueryHandler<GetPrinterDetailQuery, PrinterDetail?>
{
    public async Task<PrinterDetail?> HandleAsync(GetPrinterDetailQuery query, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(query.PrinterId, cancellationToken);
        if (printer is null)
        {
            return null;
        }

        // 012 — pure, non-throwing lookup: this printer's Brand/ModelCode already passed catalog
        // resolution at registration and are immutable thereafter.
        var model = PrinterModelCatalog.Resolve(printer.Brand, printer.ModelCode);
        var buildPlateTypeLabel = printer.BuildPlateType is { } buildPlateType
            ? BuildPlateTypeVendorNames.Resolve(buildPlateType)
            : null;
        var compatibleBuildPlateTypes = (model.CompatiblePlateTypes ?? (IReadOnlySet<BuildPlateType>)new HashSet<BuildPlateType>())
            .Select(value => new BuildPlateTypeOption(value, BuildPlateTypeVendorNames.Resolve(value)))
            .ToList();

        var printNames = await printRepository.GetCurrentPrintsAsync([printer], cancellationToken);

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
            printNames[printer.Id].Name,
            printer.FeedReadings,
            printer.BuildPlateType,
            buildPlateTypeLabel,
            compatibleBuildPlateTypes, printer.ReportedNozzleDiameterMm, printer.NozzleObservedAtUtc, printer.NozzleConfirmed, printer.ReportedState, printer.StateObservedAtUtc, printer.ActiveAlerts,
            printer.Extruders, printer.SetupIssues);
    }
}
