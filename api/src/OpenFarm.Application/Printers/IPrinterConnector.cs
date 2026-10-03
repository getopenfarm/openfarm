using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

public sealed record PrinterConnectionDetails(string Brand, string ModelCode, string IpAddress, string SerialNumber, string AccessCode);

public sealed record PrinterTelemetrySnapshot(
    decimal? NozzleTempC,
    decimal? BedTempC,
    int? PrintProgressPercent,
    string? CurrentStageLabel,
    int? RemainingTimeMinutes,
    string? CurrentJobName,
    IReadOnlyList<AmsUnit> AmsUnits,
    AmsTray? ExternalSpoolTray = null);

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

    /// <summary>
    /// This codebase's first outbound printer command (research.md R5) — pushes <paramref name="fileContent"/>
    /// to the printer and starts printing it as <paramref name="jobName"/>. Fire-and-request: the
    /// caller (<c>ProductionDispatchBackgroundService</c>) never assumes the print actually started
    /// from this call returning — only the printer's own subsequently-reported telemetry, observed
    /// through <see cref="ConnectAsync"/> and resolved by <c>IJobTelemetrySync</c>, does that
    /// (research.md R12).
    /// </summary>
    Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, CancellationToken cancellationToken);

    /// <summary>
    /// Requests the printer pause its current print. A request, not a status change — see
    /// <see cref="StartJobAsync"/>'s doc comment; the same "printer's own telemetry is the only
    /// writer" rule applies (spec Clarifications, 2026-08-07; research.md R12).
    /// </summary>
    Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken);

    /// <summary>Requests the printer resume a paused print. Same request/confirm split as <see cref="PauseJobAsync"/>.</summary>
    Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken);

    /// <summary>
    /// Requests the printer stop (cancel) its current print. Same request/confirm split as
    /// <see cref="PauseJobAsync"/> — the caller separately records that a stop was requested
    /// (<c>PrintJob.RequestCancel()</c>) so the eventual terminal telemetry resolves as
    /// <c>Cancelled</c> rather than <c>Failed</c> (FR-024c).
    /// </summary>
    Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken);
}
