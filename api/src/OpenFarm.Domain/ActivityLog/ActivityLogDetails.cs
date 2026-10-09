using OpenFarm.Domain.Printers;

namespace OpenFarm.Domain.ActivityLog;

public sealed record ActivityLogDetails(
    string? PreviousState = null,
    string? ReportedState = null,
    DateTimeOffset? ObservedAtUtc = null,
    IReadOnlyList<PrinterAlert>? PreviousAlerts = null,
    IReadOnlyList<PrinterAlert>? ActiveAlerts = null);
