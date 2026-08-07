namespace OpenFarm.Domain.Production;

public sealed class RunLineTargetNotFoundException(RunLineTargetKind targetKind, Guid targetId)
    : Exception($"{targetKind} '{targetId}' was not found in this organization's catalog.")
{
    public RunLineTargetKind TargetKind { get; } = targetKind;

    public Guid TargetId { get; } = targetId;
}
