using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <param name="Status">"Open" (Active or Complete), "Closed", "Cancelled", or null for every run.</param>
/// <param name="Search">Matches the run's name or a line's target name.</param>
public sealed record GetProductionRunsQuery(
    int Page, int PageSize, OpenFarm.Application.Common.ListSort? Sorting = null, string? Status = null, string? Search = null);

public sealed record RunLineBrief(string TargetName, int? Quantity);

/// <param name="TargetUnits">Units the finite lines ask for, every printed part counted.</param>
/// <param name="CompletedUnits">Completed units toward that target, at most the target per part.</param>
/// <param name="ProducedUnits">Units the unlimited lines have produced so far; they have no target.</param>
/// <param name="Plates">The finite lines' plates by state.</param>
/// <param name="BlockedCount">Waiting jobs whose last send to a printer failed.</param>
public sealed record ProductionRunSummary(
    Guid Id, string DisplayLabel, string? Name, ProductionRunStatus Status, DateOnly? DueDate, DateTimeOffset CreatedAtUtc,
    IReadOnlyList<RunLineBrief> Lines, decimal TargetUnits, decimal CompletedUnits, int WrittenOffUnits, int ProducedUnits,
    PlateCounts Plates, int BlockedCount);

/// <param name="StatusCounts">Runs per <c>Open</c>/<c>Closed</c>/<c>Cancelled</c> for the search, whatever status is selected.</param>
public sealed record ProductionRunsPage(IReadOnlyList<ProductionRunSummary> Items, int TotalCount, IReadOnlyDictionary<string, int> StatusCounts);
