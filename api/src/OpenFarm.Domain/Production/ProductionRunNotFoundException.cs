namespace OpenFarm.Domain.Production;

public sealed class ProductionRunNotFoundException(Guid productionRunId)
    : Exception($"Production run '{productionRunId}' was not found.")
{
    public Guid ProductionRunId { get; } = productionRunId;
}
