using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record GetProductionRunsQuery(int Page, int PageSize, OpenFarm.Application.Common.ListSort? Sorting = null);

public sealed record JobStatusSummary(int Queued, int Printing, int Paused, int Completed, int Failed, int Cancelled);

public sealed record ProductionRunSummary(
    Guid Id, string DisplayLabel, ProductionRunStatus Status, DateOnly? DueDate, int LineCount, JobStatusSummary JobSummary);

public sealed record ProductionRunsPage(IReadOnlyList<ProductionRunSummary> Items, int TotalCount);
