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
    IReadOnlyList<FeedUnitReading> FeedReadings,
    BuildPlateType? BuildPlateType,
    string? BuildPlateTypeLabel,
    IReadOnlyList<BuildPlateTypeOption> CompatibleBuildPlateTypes,
    decimal? ReportedNozzleDiameterMm = null, DateTimeOffset? NozzleObservedAtUtc = null, bool NozzleConfirmed = false,
    string? ReportedState = null, DateTimeOffset? StateObservedAtUtc = null, IReadOnlyList<PrinterAlert>? ActiveAlerts = null,
    IReadOnlyList<ExtruderReading>? Extruders = null, IReadOnlyList<string>? SetupIssues = null);

public sealed record BuildPlateTypeOption(BuildPlateType Value, string Label);
