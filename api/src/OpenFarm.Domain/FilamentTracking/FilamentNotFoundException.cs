namespace OpenFarm.Domain.FilamentTracking;

public sealed class FilamentNotFoundException(Guid filamentId)
    : Exception($"Filament '{filamentId}' was not found.")
{
    public Guid FilamentId { get; } = filamentId;
}
