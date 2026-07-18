namespace OpenFarm.Domain.FilamentTracking;

/// <summary>FR-026 — the printer's live connection has never actually reported an External Spool; there is nothing to reveal.</summary>
public sealed class ExternalSpoolNeverReportedException(Guid printerId)
    : Exception($"Printer '{printerId}' has never reported an External Spool position.")
{
    public Guid PrinterId { get; } = printerId;
}
