using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// The "by run" tree lens (single-level, lazily expanded — mirrors 013-assembly-bom's
/// AssemblyDetailPage pattern). Job-level detail is fetched separately, paginated, via the
/// print-jobs endpoints — not one deeply nested payload.
/// </summary>
public sealed record RunDetailView(
    Guid Id,
    string? Name,
    DateOnly? DueDate,
    string? Priority,
    string? Notes,
    ProductionRunStatus Status,
    IReadOnlyList<RunLineSummary> Lines);
