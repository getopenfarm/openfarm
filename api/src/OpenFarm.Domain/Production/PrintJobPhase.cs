namespace OpenFarm.Domain.Production;

/// <summary>
/// Where an open job is, finer than <see cref="PrintJobStatus"/>: a dispatched job stays <c>Queued</c>
/// until the printer reports it started, but cancelling it then stops the printer.
/// </summary>
public enum PrintJobPhase
{
    /// <summary>Waiting in the queue; not sent to a printer.</summary>
    Queued,

    /// <summary>Sent to a printer that has not reported printing yet.</summary>
    Starting,
    Printing,
    Paused,
}

public static class PrintJobPhases
{
    /// <summary>The phase of an open job; null once the job has ended.</summary>
    public static PrintJobPhase? Of(PrintJobStatus status, DateTimeOffset? dispatchedAtUtc) => status switch
    {
        PrintJobStatus.Queued => dispatchedAtUtc is null ? PrintJobPhase.Queued : PrintJobPhase.Starting,
        PrintJobStatus.Printing => PrintJobPhase.Printing,
        PrintJobStatus.Paused => PrintJobPhase.Paused,
        _ => null,
    };
}

/// <summary>The job moved on since the caller read it, so the action it confirmed no longer applies.</summary>
public sealed class PrintJobPhaseMismatchException(Guid jobId, PrintJobPhase? current)
    : Exception($"Print job '{jobId}' is now {current?.ToString() ?? "ended"}.")
{
    public Guid JobId { get; } = jobId;

    public PrintJobPhase? Current { get; } = current;
}
