namespace OpenFarm.Domain.FilamentTracking;

public sealed class SpoolConcurrencyConflictException(Guid spoolId)
    : Exception($"Spool '{spoolId}' was concurrently modified by another operation.")
{
    public Guid SpoolId { get; } = spoolId;
}
