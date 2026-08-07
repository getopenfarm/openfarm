namespace OpenFarm.Domain.Production;

public sealed class PrintJobAlreadyRequeuedException(Guid printJobId)
    : Exception($"Print job '{printJobId}' has already been requeued.")
{
    public Guid PrintJobId { get; } = printJobId;
}
