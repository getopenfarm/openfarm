namespace OpenFarm.Domain.Production;

/// <summary>
/// Guarded transition table for <see cref="PrintJobStatus"/>, mirroring
/// <c>PrinterStatusTransitions</c>'s own shape (data-model.md).
/// </summary>
public static class PrintJobStatusTransitions
{
    private static readonly HashSet<(PrintJobStatus From, PrintJobStatus To)> LegalTransitions =
    [
        (PrintJobStatus.Queued, PrintJobStatus.Printing),
        (PrintJobStatus.Printing, PrintJobStatus.Paused),
        (PrintJobStatus.Paused, PrintJobStatus.Printing),
        (PrintJobStatus.Printing, PrintJobStatus.Failed),
        (PrintJobStatus.Paused, PrintJobStatus.Failed),
        (PrintJobStatus.Printing, PrintJobStatus.Completed),
        (PrintJobStatus.Paused, PrintJobStatus.Completed),
        (PrintJobStatus.Queued, PrintJobStatus.Cancelled),
        (PrintJobStatus.Printing, PrintJobStatus.Cancelled),
        (PrintJobStatus.Paused, PrintJobStatus.Cancelled),
    ];

    public static void EnsureValidTransition(PrintJobStatus from, PrintJobStatus to)
    {
        if (!LegalTransitions.Contains((from, to)))
        {
            throw new InvalidPrintJobStatusTransitionException(from, to);
        }
    }
}
