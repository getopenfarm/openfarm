using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// A requirement's or run's plates by state — what its progress strip draws. A lost plate that was reprinted is left
/// out (its replacement counts instead); one still awaiting reprint or write-off is <see cref="NeedsAction"/>.
/// </summary>
public sealed record PlateCounts(int Completed, int Printing, int Queued, int NeedsAction, int WrittenOff)
{
    public static readonly PlateCounts None = new(0, 0, 0, 0, 0);

    public static PlateCounts Of(IEnumerable<PrintJob> jobs)
    {
        int completed = 0, printing = 0, queued = 0, needsAction = 0, writtenOff = 0;
        foreach (var job in jobs)
        {
            if (job.WrittenOffAtUtc is not null) writtenOff++;
            else if (job.IsUnresolvedLoss) needsAction++;
            else if (job.Status == PrintJobStatus.Completed) completed++;
            else if (job.Status is PrintJobStatus.Printing or PrintJobStatus.Paused) printing++;
            else if (job.Status == PrintJobStatus.Queued) queued++;
        }

        return new(completed, printing, queued, needsAction, writtenOff);
    }

    public PlateCounts Add(PlateCounts other) => new(
        Completed + other.Completed, Printing + other.Printing, Queued + other.Queued, NeedsAction + other.NeedsAction,
        WrittenOff + other.WrittenOff);
}
