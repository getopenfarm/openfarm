namespace OpenFarm.Domain.Production;

public sealed class PrintJobAlreadyDispatchedException(Guid printJobId)
    : Exception($"Print job '{printJobId}' is no longer Queued, so this action no longer applies.")
{
    public Guid PrintJobId { get; } = printJobId;
}
