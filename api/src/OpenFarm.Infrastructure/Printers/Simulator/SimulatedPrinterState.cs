using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Infrastructure.Printers.Simulator;

/// <summary>
/// Per-serial-number in-memory state for a simulated printer, owned by
/// <see cref="SimulatorPrinterConnector"/>'s per-serial dictionary. Never persisted — data-model.md
/// confirms simulated state is non-durable (a restart re-establishes Idle).
/// </summary>
public sealed class SimulatedPrinterState
{
    private const decimal IdleNozzleTempC = 25m;
    private const decimal IdleBedTempC = 25m;

    public required string SerialNumber { get; init; }

    public required TimeSpan CycleDuration { get; init; }

    public PrinterStatus Status { get; set; } = PrinterStatus.Idle;

    public DateTimeOffset? JobStartedAtUtc { get; set; }

    public decimal NozzleTempC { get; set; } = IdleNozzleTempC;

    public decimal BedTempC { get; set; } = IdleBedTempC;

    public int PrintProgressPercent { get; set; }

    /// <summary>
    /// Illustrative "current job" the simulated printer reports while printing — stands in for the
    /// <c>subtask_name</c> a real Bambu printer sends, so the dashboard/grid card exercise the
    /// job-name + ETA fields end to end. Null when idle (nothing on the bed).
    /// </summary>
    public string? CurrentJobName { get; set; }

    /// <summary>Minutes left in the simulated print, counted down from the cycle duration.</summary>
    public int? RemainingTimeMinutes { get; set; }

    /// <summary>
    /// Set by <see cref="SimulatorPrinterConnector.PauseJobAsync"/>/<see cref="SimulatorPrinterConnector.ResumeJobAsync"/>
    /// (research.md R12) — <see cref="SimulatorPrinterConnector.Advance"/> stops progressing
    /// temps/percent/remaining-time while <see langword="true"/>, mirroring a real paused print.
    /// </summary>
    public bool IsPaused { get; set; }

    /// <summary>
    /// A raw gcode state string queued by an outbound command (Start/Pause/Resume/Stop) or by
    /// <see cref="SimulatorPrinterConnector.Advance"/> reaching a genuine phase transition, consumed
    /// and cleared by the next <see cref="SimulatorPrinterConnector.ConnectAsync"/> tick. Distinct from
    /// re-deriving a gcode string from <see cref="Status"/> alone, since a pause/resume flip changes
    /// the raw reported state without ever changing <see cref="Status"/> itself (Printer has no Paused
    /// member — research.md R12's own finding).
    /// </summary>
    public string? PendingGcodeState { get; set; }

    /// <summary>
    /// Fixed illustrative default fixture (data-model.md's AMS default content table) — same static
    /// material/color/remaining-% shape for the life of the connection; only the printing tray's
    /// <c>Active</c> flag reflects current status. Slots 1 and 2 (indexes 0 and 1) hold RFID-tagged spools, tagged per printer so two simulated
    /// printers never read the same spool. The virtual printer also reports an External Spool position, letting
    /// quickstart.md Scenario 2's default-hidden-behind-the-active-AMS behavior be observed locally without real hardware.
    /// </summary>
    public IReadOnlyList<FeedUnitReading> FeedReadings =>
    [
        new FeedUnitReading(FeedUnitKindCatalog.MultiMaterialUnit, 0,
        [
            new FeedSlotReading(0, Empty: false, MaterialType: "PLA", ColorHex: "#E53935", SubBrand: "Generic", RemainPercent: 82, Active: Status == PrinterStatus.Printing,
                RfidId: $"{SerialNumber}-0", RfidWeightGrams: 1000),
            new FeedSlotReading(1, Empty: false, MaterialType: "PETG", ColorHex: "#1E88E5", SubBrand: "Generic", RemainPercent: 45, Active: false,
                RfidId: $"{SerialNumber}-1", RfidWeightGrams: 1000),
            new FeedSlotReading(2, Empty: false, MaterialType: "ABS", ColorHex: "#43A047", SubBrand: "Generic", RemainPercent: 90, Active: false),
            new FeedSlotReading(3, Empty: false, MaterialType: "TPU", ColorHex: "#FDD835", SubBrand: "Generic", RemainPercent: 12, Active: false),
        ]),
        new FeedUnitReading(FeedUnitKindCatalog.ExternalSpool, 0,
            [new FeedSlotReading(0, Empty: false, MaterialType: "PLA", ColorHex: "#8E24AA", SubBrand: "Generic", RemainPercent: 60, Active: false)]),
    ];
}
