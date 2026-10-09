using OpenFarm.Application.Common;
using OpenFarm.Domain.ActivityLog;

namespace OpenFarm.Application.ActivityLog;

public sealed record GetActivityLogQuery(int Page, int PageSize, string? EntityType = null, string? EntityId = null);

public sealed record ActivityLogEntryResult(
    Guid Id,
    string EntityType,
    string EntityId,
    string EventType,
    Guid? ActorId,
    ActorType ActorType,
    string? ActorDisplayName,
    DateTimeOffset TimestampUtc, ActivityLogDetails? Details = null);

public sealed record ActivityLogPageResult(IReadOnlyList<ActivityLogEntryResult> Items, int Page, int PageSize, int TotalCount);

public sealed class GetActivityLogQueryHandler(IActivityLogReader reader) : IQueryHandler<GetActivityLogQuery, ActivityLogPageResult>
{
    public Task<ActivityLogPageResult> HandleAsync(GetActivityLogQuery query, CancellationToken cancellationToken) =>
        reader.GetPageAsync(query.Page, query.PageSize, cancellationToken, query.EntityType, query.EntityId);
}
