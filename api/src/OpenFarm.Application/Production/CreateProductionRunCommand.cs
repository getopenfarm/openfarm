using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record CreateProductionRunCommand(
    string? Name,
    DateOnly? DueDate,
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
    int? BackfillCumulativeUnitCeiling)
{
    public RunLineSpec ToSpec() => new(
        TargetKind, TargetId, Quantity, RevisionMode, DefaultAssignmentMode, DefaultPinnedPrinterId, BackfillPrinterCap,
        BackfillCumulativeUnitCeiling);
}
