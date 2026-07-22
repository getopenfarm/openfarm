using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

public sealed record GetPrinterDetailQuery(Guid PrinterId);

public sealed record PrinterDetail(
    Guid Id,
    string Name,
    string Brand,
    string ModelCode,
    string IpAddress,
    string SerialNumber,
    PrinterStatus Status,
    bool IsReachable,
    decimal? NozzleTempC,
    decimal? BedTempC,
    int? PrintProgressPercent,
    string? CurrentStageLabel,
    int? RemainingTimeMinutes,
    string? CurrentJobName,
    IReadOnlyList<AmsUnit> AmsUnits,
    Guid? MachineProfileId,
    string? MachineProfileName,
    Guid? ProcessProfileId,
    string? ProcessProfileName);
