using OpenFarm.Application.Common;
using OpenFarm.Application.Production;

namespace OpenFarm.Application.Printers;

public sealed class GetFleetQueryHandler(IPrinterRepository printerRepository, IPrintRepository printRepository)
    : IQueryHandler<GetFleetQuery, IReadOnlyList<FleetPrinterSummary>>
{
    public async Task<IReadOnlyList<FleetPrinterSummary>> HandleAsync(GetFleetQuery query, CancellationToken cancellationToken)
    {
        var printers = await printerRepository.ListAsync(cancellationToken);
        var printNames = await printRepository.GetCurrentPrintNamesAsync(printers, cancellationToken);

        return printers
            .Select(p => new FleetPrinterSummary(
                p.Id,
                p.Name,
                p.Brand,
                p.ModelCode,
                p.IpAddress,
                p.SerialNumber,
                p.Status,
                p.IsReachable,
                p.PrintProgressPercent,
                p.NozzleTempC,
                p.BedTempC,
                p.CurrentStageLabel,
                p.RemainingTimeMinutes,
                printNames[p.Id],
                FilamentColor.FromFeedReadings(p.FeedReadings), p.ReportedNozzleDiameterMm, p.NozzleObservedAtUtc, p.NozzleConfirmed))
            .ToList();
    }
}
