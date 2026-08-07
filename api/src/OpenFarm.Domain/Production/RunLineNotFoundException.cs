namespace OpenFarm.Domain.Production;

public sealed class RunLineNotFoundException(Guid runLineId)
    : Exception($"Run line '{runLineId}' was not found.")
{
    public Guid RunLineId { get; } = runLineId;
}
