namespace OpenFarm.Domain.FilamentTracking;

public sealed class FilamentVariantNotFoundException(Guid filamentVariantId)
    : Exception($"Filament variant '{filamentVariantId}' was not found.")
{
    public Guid FilamentVariantId { get; } = filamentVariantId;
}
