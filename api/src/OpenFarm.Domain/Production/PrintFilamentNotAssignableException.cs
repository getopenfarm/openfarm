namespace OpenFarm.Domain.Production;

public sealed class PrintFilamentNotAssignableException(Guid printId, string reason)
    : Exception($"A spool cannot be assigned to this filament line of print '{printId}': {reason}")
{
    public Guid PrintId { get; } = printId;
}
