using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record RunLineSummary(
    Guid Id,
    RunLineTargetKind TargetKind,
    Guid TargetId,
    string TargetName,
    int? Quantity,
    RevisionMode RevisionMode,
    IReadOnlyList<PartRequirementSummary> Requirements);
