using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record CreateProductionRunCommand(
    string? Name,
    DateOnly? DueDate,
    string? Priority,
    string? Notes,
    IReadOnlyList<RunLineInput> Lines);

public sealed record RunLineInput(
    RunLineTargetKind TargetKind,
    Guid TargetId,
    int? Quantity,
    RevisionMode? RevisionMode,
    AssignmentMode DefaultAssignmentMode,
    Guid? DefaultPinnedPrinterId,
    int BackfillPrinterCap,
    int? BackfillCumulativeUnitCeiling);
