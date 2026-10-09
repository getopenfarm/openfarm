namespace OpenFarm.Application.Production;

public sealed record GetQueueJobsQuery(int Page, int PageSize, bool BackfillOnly = false, string? Status = null, string? Search = null);

public sealed record QueueJobsPage(IReadOnlyList<QueueJobRow> Items, int TotalCount, IReadOnlyDictionary<string, int> StatusCounts);
