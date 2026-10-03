namespace OpenFarm.Domain.Production;

public sealed class PrintJobNotFoundException(Guid printJobId)
    : Exception($"Print job '{printJobId}' was not found.")
{
    public Guid PrintJobId { get; } = printJobId;
}
