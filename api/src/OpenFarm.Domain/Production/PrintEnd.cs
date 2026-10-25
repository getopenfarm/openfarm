namespace OpenFarm.Domain.Production;

/// <summary>How and when a print ended that OpenFarm didn't see end live: the printer came back idle, or reported its end
/// only after a silence (it was offline, or OpenFarm was down).</summary>
public static class PrintEnd
{
    /// <summary>Progress at or above which a print that stopped reporting counts as finished.</summary>
    public const int FinishedPercent = 99;

    /// <summary>The last progress seen says the print got to its end: its percent, or its last layer.</summary>
    public static bool LooksFinished(int? percent, int? layer, int? totalLayers) =>
        percent >= FinishedPercent || (layer is > 0 && totalLayers is > 0 && layer >= totalLayers);

    /// <summary>
    /// A finished print ended when the time it had left at <paramref name="lastHeardUtc"/> ran out, never after now; any
    /// other outcome is placed when the printer was last heard, the latest moment it is known to have been printing.
    /// </summary>
    public static DateTimeOffset Infer(PrintJobStatus outcome, DateTimeOffset lastHeardUtc, int? remainingMinutes, DateTimeOffset nowUtc)
    {
        var end = outcome == PrintJobStatus.Completed && remainingMinutes is { } remaining ? lastHeardUtc.AddMinutes(remaining) : lastHeardUtc;
        return end < nowUtc ? end : nowUtc;
    }
}
