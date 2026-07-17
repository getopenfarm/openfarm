using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

public sealed record PrinterConnectionDetails(string Brand, string ModelCode, string IpAddress, string SerialNumber, string AccessCode);

public sealed record PrinterTelemetrySnapshot(
    decimal? NozzleTempC,
    decimal? BedTempC,
    int? PrintProgressPercent,
    string? CurrentStageLabel,
    IReadOnlyList<AmsUnit> AmsUnits);

/// <summary>
/// One inbound message from a printer's live connection, already parsed into domain-shaped data —
/// never raw MQTT JSON (plan.md's Clean Architecture gate). <see cref="GcodeState"/> is the raw
/// connector-reported state string (e.g. "RUNNING"); resolving it to a <see cref="PrinterStatus"/>
/// is <see cref="PrinterStatusTransitions.ObserveStatus"/>'s job, not the connector's. Camera frames
/// are a separate stream (<see cref="IPrinterCameraRelay"/>), not part of this event type.
/// </summary>
public sealed record PrinterConnectorEvent(
    DateTimeOffset ReceivedAtUtc,
    string? GcodeState = null,
    PrinterTelemetrySnapshot? Telemetry = null);

/// <summary>
/// Application-defined contract for a printer brand's live connection, implemented in
/// Infrastructure (e.g. <c>BambuLanConnector</c>). Domain/Application never reference the
/// underlying protocol (MQTTnet, TLS, etc.) directly.
/// </summary>
public interface IPrinterConnector
{
    /// <summary>
    /// Short-lived connect + authenticate check (research.md §7) — never persists anything and
    /// always disconnects before returning, regardless of outcome.
    /// </summary>
    Task<bool> VerifyAsync(PrinterConnectionDetails details, CancellationToken cancellationToken);

    /// <summary>
    /// Long-lived event stream consumed by <c>PrinterConnectionBackgroundService</c>. Reconnect/backoff
    /// is the implementation's responsibility; the stream yields one event per successfully parsed
    /// message for as long as the connector stays connected, and completes when
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    IAsyncEnumerable<PrinterConnectorEvent> ConnectAsync(PrinterConnectionDetails details, CancellationToken cancellationToken);
}
