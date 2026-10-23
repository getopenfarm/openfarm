using OpenFarm.Domain.ActivityLog;

namespace OpenFarm.Application.ActivityLog;

public interface IActivityLogWriter
{
    Task WriteAsync(ActivityLogEntry entry, CancellationToken cancellationToken);

    /// <summary>Several entries from one request; the EF writer saves them at once, since a delivery can add a thousand spools.</summary>
    async Task WriteRangeAsync(IReadOnlyCollection<ActivityLogEntry> entries, CancellationToken cancellationToken)
    {
        foreach (var entry in entries) await WriteAsync(entry, cancellationToken);
    }
}
