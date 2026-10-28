namespace OpenFarm.Domain.Production;

public sealed class ProductionRunNotOpenException(Guid productionRunId)
    : Exception($"Production run '{productionRunId}' is closed or cancelled.")
{
    public Guid ProductionRunId { get; } = productionRunId;
}
