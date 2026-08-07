namespace OpenFarm.Domain.Production;

public sealed class InvalidRunCompletionException(Guid productionRunId)
    : Exception($"Production run '{productionRunId}' cannot complete while a finite requirement remains unsatisfied.")
{
    public Guid ProductionRunId { get; } = productionRunId;
}
