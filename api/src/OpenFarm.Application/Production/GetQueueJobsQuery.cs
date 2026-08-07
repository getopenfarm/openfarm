namespace OpenFarm.Application.Production;

public sealed record GetQueueJobsQuery(int Page, int PageSize, bool BackfillOnly = false);

public sealed record QueueJobsPage(IReadOnlyList<QueueJobRow> Items, int TotalCount);
