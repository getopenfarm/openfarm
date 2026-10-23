using OpenFarm.Application.ActivityLog;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.ActivityLog;

public sealed class EfActivityLogWriter(OpenFarmDbContext dbContext) : IActivityLogWriter
{
    public async Task WriteAsync(ActivityLogEntry entry, CancellationToken cancellationToken)
    {
        dbContext.ActivityLogEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task WriteRangeAsync(IReadOnlyCollection<ActivityLogEntry> entries, CancellationToken cancellationToken)
    {
        dbContext.ActivityLogEntries.AddRange(entries);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
