using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

public sealed record GetFleetQuery;

public sealed record FleetPrinterSummary(
    Guid Id,
    string Name,
    string Brand,
    string ModelCode,
    string IpAddress,
    string SerialNumber,
    PrinterStatus Status,
    bool IsReachable,
    int? PrintProgressPercent);
