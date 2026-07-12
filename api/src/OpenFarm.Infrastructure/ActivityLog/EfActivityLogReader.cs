using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.ActivityLog;

public sealed class EfActivityLogReader(OpenFarmDbContext dbContext) : IActivityLogReader
{
    public async Task<ActivityLogPageResult> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.ActivityLogEntries.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        var entries = await query
            .OrderByDescending(e => e.TimestampUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var actorIds = entries
            .Where(e => e.ActorId.HasValue)
            .Select(e => e.ActorId!.Value)
            .Distinct()
            .ToList();

        var actorNames = await dbContext.Users
            .AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName ?? u.Email ?? u.Id.ToString(), cancellationToken);

        var items = entries
            .Select(e => new ActivityLogEntryResult(
                e.Id,
                e.EntityType,
                e.EntityId,
                e.EventType,
                e.ActorId,
                e.ActorType,
                e.ActorId.HasValue && actorNames.TryGetValue(e.ActorId.Value, out var name) ? name : null,
                e.TimestampUtc))
            .ToList();

        return new ActivityLogPageResult(items, page, pageSize, totalCount);
    }
}
