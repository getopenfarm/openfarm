using OpenFarm.Domain.ActivityLog;

namespace OpenFarm.Application.ActivityLog;

public interface IActivityLogWriter
{
    Task WriteAsync(ActivityLogEntry entry, CancellationToken cancellationToken);
}
