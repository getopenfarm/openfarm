namespace OpenFarm.Domain.Printers;

/// <summary>
/// Strict manual transition rules (<see cref="EnsureValidManualTransition"/>) vs. lenient
/// connector-reported reconciliation (<see cref="ObserveStatus"/>), per data-model.md's
/// PrinterStatus transition table and research.md §2's gcode_state mapping.
/// </summary>
public static class PrinterStatusTransitions
{
    /// <summary>
    /// The only manual transition this phase supports (FR-011: no manual print dispatch).
    /// </summary>
    public static void EnsureValidManualTransition(PrinterStatus from, PrinterStatus to)
    {
        if (from != PrinterStatus.AwaitingClear || to != PrinterStatus.Idle)
        {
            throw new InvalidPrinterStatusTransitionException(from, to);
        }
    }

    /// <summary>
    /// Maps a connector-reported <c>gcode_state</c> to <see cref="PrinterStatus"/>. Never throws —
    /// unmapped values (<c>offline</c>, <c>unknown</c>, or anything unrecognized) return
    /// <see langword="null"/>, meaning "no status change should be applied" (reachability is
    /// tracked separately, per research.md §2).
    /// </summary>
    public static PrinterStatus? ObserveStatus(string gcodeState) =>
        gcodeState.Trim().ToUpperInvariant() switch
        {
            "IDLE" => PrinterStatus.Idle,
            "INIT" or "PREPARE" or "SLICING" or "RUNNING" or "PAUSE" => PrinterStatus.Printing,
            "FINISH" or "FAILED" => PrinterStatus.AwaitingClear,
            _ => null,
        };
}
