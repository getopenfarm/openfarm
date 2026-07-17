namespace OpenFarm.Application.Profiles;

public sealed class FilamentProfileNotFoundException(Guid filamentProfileId)
    : Exception($"Filament profile '{filamentProfileId}' was not found.")
{
    public Guid FilamentProfileId { get; } = filamentProfileId;
}
