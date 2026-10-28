namespace OpenFarm.Domain.Production;

public sealed class PrintJobNotEligibleForWriteOffException(Guid printJobId)
    : Exception($"Print job '{printJobId}' is not a failed or cancelled plate awaiting a decision.")
{
    public Guid PrintJobId { get; } = printJobId;
}
