namespace OpenFarm.Domain.Printers;

/// <summary>
/// What a printer's time went to, for stats: the open stretch is <see cref="Printer.Activity"/> since
/// <see cref="Printer.ActivitySinceUtc"/>, each earlier one a <see cref="PrinterActivitySpan"/>.
/// </summary>
public enum PrinterActivity
{
    Printing,
    Paused,

    /// <summary>A print ended and the plate waits for Mark ready, reachable or not.</summary>
    AwaitingClear,
    Idle,
    Offline,

    /// <summary>OpenFarm wasn't running, so nothing was observed.</summary>
    Unknown,
}

/// <summary>One closed stretch of a printer's activity. Append-only; written when the printer moves on.</summary>
public sealed class PrinterActivitySpan
{
    private PrinterActivitySpan(Guid id, Guid printerId, PrinterActivity activity, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc)
    {
        Id = id;
        PrinterId = printerId;
        Activity = activity;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
    }

    public Guid Id { get; }

    public Guid PrinterId { get; }

    public PrinterActivity Activity { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset EndedAtUtc { get; }

    internal static PrinterActivitySpan Closed(Guid printerId, PrinterActivity activity, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc) =>
        new(Guid.NewGuid(), printerId, activity, startedAtUtc, endedAtUtc);
}
