using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>The run lens: run → line → requirement → its plates (jobs), in one payload.</summary>
public sealed record RunDetailView(
    Guid Id,
    string DisplayLabel,
    string? Name,
    DateOnly? DueDate,
    string? Notes,
    ProductionRunStatus Status,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<RunLineSummary> Lines);
