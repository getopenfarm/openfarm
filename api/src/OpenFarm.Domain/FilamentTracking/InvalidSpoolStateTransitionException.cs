namespace OpenFarm.Domain.FilamentTracking;

public sealed class InvalidSpoolStateTransitionException(SpoolState from, SpoolState to)
    : Exception($"Cannot transition a spool from '{from}' to '{to}'.")
{
    public SpoolState From { get; } = from;

    public SpoolState To { get; } = to;
}
