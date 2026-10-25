namespace OpenFarm.Application.Production;

/// <summary>
/// The single writer of every telemetry-observable <see cref="Domain.Production.PrintJob"/>
/// transition for a live-integrated printer (research.md R12) — invoked directly from
/// <c>PrinterConnectionBackgroundService.HandleEventAsync</c>, alongside the existing
/// <c>IFeedSlotTelemetrySync</c> call, on every printer event. A command handler never calls a
/// <c>PrintJob.Mark*</c> method itself for a live-integrated job; only this observes the printer's
/// own reported reality and drives the matching transition.
/// </summary>
public interface IJobTelemetrySync
{
    /// <param name="unseenSinceUtc">
    /// When the printer was last heard, if nothing was observed between then and this report (it was unreachable, or
    /// OpenFarm was down); null while it was being watched. A print found ended is dated back into that silence.
    /// </param>
    Task ObserveAsync(Guid printerId, string gcodeState, DateTimeOffset? unseenSinceUtc, CancellationToken cancellationToken);
}
