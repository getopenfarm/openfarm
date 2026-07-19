namespace OpenFarm.Domain.FilamentTracking;

public sealed class DuplicateFilamentVariantColorException(Guid filamentId, string colorName)
    : Exception($"Filament '{filamentId}' already has a variant named '{colorName}'.")
{
    public Guid FilamentId { get; } = filamentId;

    public string ColorName { get; } = colorName;
}
