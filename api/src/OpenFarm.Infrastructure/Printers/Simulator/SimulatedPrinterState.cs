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
    /// Fixed illustrative default fixture (data-model.md's AMS default content table) — same static
    /// material/color/remaining-% shape for the life of the connection; only the printing tray's
    /// <c>Active</c> flag reflects current status.
    /// </summary>
    public IReadOnlyList<AmsUnit> AmsUnits =>
    [
        new AmsUnit(0,
        [
            new AmsTray(0, Empty: false, MaterialType: "PLA", ColorHex: "#E53935", SubBrand: "Generic", RemainPercent: 82, Active: Status == PrinterStatus.Printing),
            new AmsTray(1, Empty: false, MaterialType: "PETG", ColorHex: "#1E88E5", SubBrand: "Generic", RemainPercent: 45, Active: false),
            new AmsTray(2, Empty: false, MaterialType: "ABS", ColorHex: "#43A047", SubBrand: "Generic", RemainPercent: 90, Active: false),
            new AmsTray(3, Empty: false, MaterialType: "TPU", ColorHex: "#FDD835", SubBrand: "Generic", RemainPercent: 12, Active: false),
        ]),
    ];
}
