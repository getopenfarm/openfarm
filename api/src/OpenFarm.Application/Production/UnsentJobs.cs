using OpenFarm.Application.ActivityLog;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// FR-017/FR-024d: ending a run (cancel or close) cancels its Queued jobs no printer has been given yet; a job already
/// sent stays and still records its outcome — ending a run never sends a printer command.
/// </summary>
internal static class UnsentJobs
{
    public static List<PrintJob> Cancel(IEnumerable<PrintJob> jobs)
    {
        var unsent = jobs.Where(j => j.Status == PrintJobStatus.Queued && j.PrinterId is null).ToList();
        foreach (var job in unsent)
        {
            job.MarkCancelled();
        }

        return unsent;
    }

    /// <summary>After the atomic business/audit save: one hub event per cancelled job.</summary>
    public static async Task PublishAsync(
        IReadOnlyList<PrintJob> cancelled, Guid? actorId, IProductionEventPublisher publisher, IActivityLogWriter? activityLogWriter,
        CancellationToken cancellationToken)
    {
        foreach (var job in cancelled)
        {
            await publisher.PrintJobStatusChangedAsync(job.Id, job.Status, cancellationToken);
        }
    }
}
