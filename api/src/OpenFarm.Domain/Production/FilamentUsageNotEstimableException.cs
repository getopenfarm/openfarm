namespace OpenFarm.Domain.Production;

public sealed class FilamentUsageNotEstimableException(Guid printId)
    : Exception($"Filament usage for print '{printId}' cannot be estimated. Record the usage on the spool manually.")
{
    public Guid PrintId { get; } = printId;
}
