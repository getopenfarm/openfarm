namespace OpenFarm.Application.ActivityLog;

public interface IActivityLogReader
{
    Task<ActivityLogPageResult> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken, string? entityType = null, string? entityId = null);
}
