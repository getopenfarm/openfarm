namespace OpenFarm.Domain.Production;

public sealed class PrintJobNotEligibleForRequeueException(Guid printJobId)
    : Exception($"Print job '{printJobId}' is not eligible for requeue.")
{
    public Guid PrintJobId { get; } = printJobId;
}
