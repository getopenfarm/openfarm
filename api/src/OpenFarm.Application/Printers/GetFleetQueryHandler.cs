using OpenFarm.Application.Common;

namespace OpenFarm.Application.Printers;

public sealed class GetFleetQueryHandler(IPrinterRepository printerRepository)
    : IQueryHandler<GetFleetQuery, IReadOnlyList<FleetPrinterSummary>>
{
    public async Task<IReadOnlyList<FleetPrinterSummary>> HandleAsync(GetFleetQuery query, CancellationToken cancellationToken)
    {
        var printers = await printerRepository.ListAsync(cancellationToken);

        return printers
            .Select(p => new FleetPrinterSummary(p.Id, p.Name, p.Brand, p.ModelCode, p.IpAddress, p.SerialNumber, p.Status, p.IsReachable, p.PrintProgressPercent, p.NozzleTempC, p.BedTempC))
            .ToList();
    }
}
