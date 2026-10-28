namespace OpenFarm.Domain.Production;

/// <summary>A line whose target explodes to no printed parts would have no plates, so its run could never complete.</summary>
public sealed class RunLineHasNoPrintedPartsException(Guid targetId)
    : Exception($"Run line target '{targetId}' has no printed parts.")
{
    public Guid TargetId { get; } = targetId;
}
