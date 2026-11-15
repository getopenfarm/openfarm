namespace OpenFarm.Domain.Printers;

/// <summary>
/// Strict manual transition rules (<see cref="EnsureValidManualTransition"/>) vs. lenient
/// connector-reported reconciliation (<see cref="ObserveStatus"/>), per data-model.md's
/// PrinterStatus transition table and research.md §2's gcode_state mapping.
/// </summary>
public static class PrinterStatusTransitions
{
    /// <summary>
    /// The only manual transitions (FR-011: no manual print dispatch): Mark ready (AwaitingClear -&gt; Idle) and
    /// Mark not ready, an operator hold (Idle -&gt; AwaitingClear). A Printing printer cannot be held: its print end
    /// already makes it Not ready.
    /// </summary>
    public static void EnsureValidManualTransition(PrinterStatus from, PrinterStatus to)
    {
        var markReady = from == PrinterStatus.AwaitingClear && to == PrinterStatus.Idle;
        var hold = from == PrinterStatus.Idle && to == PrinterStatus.AwaitingClear;
        if (!markReady && !hold)
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
