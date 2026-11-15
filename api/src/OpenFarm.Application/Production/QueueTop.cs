using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>Puts jobs, in their order, ahead of every waiting job: Move run to top and Print at top.</summary>
internal static class QueueTop
{
    private const double Spacing = 1024;

    public static void Place(IReadOnlyList<PrintJob> jobs, double? minQueuedPosition)
    {
        var top = minQueuedPosition ?? Spacing;
        for (var i = 0; i < jobs.Count; i++) jobs[i].Reorder(top - (jobs.Count - i) * Spacing);
    }
}
