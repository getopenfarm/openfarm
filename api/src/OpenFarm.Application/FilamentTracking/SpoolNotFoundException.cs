namespace OpenFarm.Application.FilamentTracking;

public sealed class SpoolNotFoundException(Guid spoolId) : Exception($"Spool '{spoolId}' was not found.")
{
    public Guid SpoolId { get; } = spoolId;
}
