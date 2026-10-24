using OpenFarm.Application.Common;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Printers;

public sealed class GetPrinterDetailQueryHandler(
    IPrinterRepository printerRepository,
    IMachineProfileRepository machineProfileRepository,
    IProcessProfileRepository processProfileRepository, ProfileCompatibility compatibility)
    : IQueryHandler<GetPrinterDetailQuery, PrinterDetail?>
{
    public async Task<PrinterDetail?> HandleAsync(GetPrinterDetailQuery query, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(query.PrinterId, cancellationToken);
        if (printer is null)
        {
            return null;
        }

        // The link can dangle if its Machine/Process Profile was deleted independently — treated as
        // unlinked for display purposes rather than surfacing as an error here.
        var machineProfileName = printer.MachineProfileId is { } machineProfileId
            ? (await machineProfileRepository.GetByIdAsync(machineProfileId, cancellationToken))?.Name
            : null;

        string? machineProfileIssue = null;
        if (printer.MachineProfileId is { } selectedId)
        {
            var machine = await machineProfileRepository.GetByIdAsync(selectedId, cancellationToken);
            if (machine is null) machineProfileIssue = "The linked machine profile no longer exists.";
            else
                try { await compatibility.ValidateMachineAsync(printer, machine, cancellationToken); }
                catch (SlicingProfileUnconfiguredException ex) { machineProfileIssue = ex.Message; }
        }

        var processProfileName = printer.ProcessProfileId is { } processProfileId
            ? (await processProfileRepository.GetByIdAsync(processProfileId, cancellationToken))?.Name
            : null;

        // 012 — pure, non-throwing lookup: this printer's Brand/ModelCode already passed catalog
        // resolution at registration and are immutable thereafter (unlike the dangling-FK tolerance
        // machine/process profile names need above).
        var model = PrinterModelCatalog.Resolve(printer.Brand, printer.ModelCode);
        var buildPlateTypeLabel = printer.BuildPlateType is { } buildPlateType
            ? BuildPlateTypeVendorNames.Resolve(buildPlateType)
            : null;
        var compatibleBuildPlateTypes = (model.CompatiblePlateTypes ?? (IReadOnlySet<BuildPlateType>)new HashSet<BuildPlateType>())
            .Select(value => new BuildPlateTypeOption(value, BuildPlateTypeVendorNames.Resolve(value)))
            .ToList();

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
            printer.FeedReadings,
            printer.MachineProfileId,
            machineProfileName,
            printer.ProcessProfileId,
            processProfileName,
            printer.BuildPlateType,
            buildPlateTypeLabel,
            compatibleBuildPlateTypes, printer.ReportedNozzleDiameterMm, printer.NozzleObservedAtUtc, printer.NozzleConfirmed, machineProfileIssue, printer.ReportedState, printer.StateObservedAtUtc, printer.ActiveAlerts,
            printer.Extruders);
    }
}
