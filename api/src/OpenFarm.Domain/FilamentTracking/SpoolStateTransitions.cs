namespace OpenFarm.Domain.FilamentTracking;

/// <summary>Mirrors <see cref="OpenFarm.Domain.Printers.PrinterStatusTransitions"/>'s shape.</summary>
public static class SpoolStateTransitions
{
    public static void EnsureValidTransition(SpoolState from, SpoolState to)
    {
        var valid = (from, to) switch
        {
            (SpoolState.InStorage, SpoolState.Using) => true,
            (SpoolState.Using, SpoolState.InStorage) => true,
            (SpoolState.Using, SpoolState.Finished) => true,
            (SpoolState.InStorage, SpoolState.Finished) => true,
            (SpoolState.Finished, SpoolState.InStorage) => true,
            _ => false,
        };

        if (!valid)
        {
            throw new InvalidSpoolStateTransitionException(from, to);
        }
    }

    /// <summary>Guards an operation (e.g. recording usage) that requires the spool already be <see cref="SpoolState.Using"/>, without itself being a transition.</summary>
    public static void EnsureUsing(SpoolState state)
    {
        if (state != SpoolState.Using)
        {
            throw new InvalidSpoolStateTransitionException(state, SpoolState.Using);
        }
    }
}
