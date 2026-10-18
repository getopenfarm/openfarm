using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// Syncs a printer's connector-derived <c>MultiMaterialUnit</c>/<c>ExternalSpool</c> <c>FeedUnit</c>/
/// <c>FeedSlot</c> rows from its live-reported topology (research.md §6). Never touches
/// <c>AutomaticFilamentReloader</c> rows.
/// </summary>
public interface IFeedSlotTelemetrySync
{
    Task SyncAsync(
        Guid printerId,
        string brand,
        IReadOnlyList<AmsUnit> amsUnits,
        AmsTray? externalSpoolTray,
        CancellationToken cancellationToken);
}
