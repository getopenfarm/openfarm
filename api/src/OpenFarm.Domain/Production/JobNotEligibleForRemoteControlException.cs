namespace OpenFarm.Domain.Production;

public sealed class JobNotEligibleForRemoteControlException(Guid printJobId)
    : Exception($"Print job '{printJobId}' is not eligible for this remote-control action.")
{
    public Guid PrintJobId { get; } = printJobId;
}
