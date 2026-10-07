using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Application.Parts;

public sealed class InvalidPartFilamentSelectionException(string message) : Exception(message);

public sealed class PartFilamentSelectionValidator(IFilamentRepository filaments)
{
    public async Task ValidateAsync(string? materialType, Guid? variantId, CancellationToken ct)
    {
        if (variantId is null) return;
        var variant = await filaments.FindVariantByIdAsync(variantId.Value, ct)
            ?? throw new InvalidPartFilamentSelectionException("The selected catalog filament colour variant no longer exists.");
        var filament = await filaments.GetByIdAsync(variant.FilamentId, ct)
            ?? throw new InvalidPartFilamentSelectionException("The selected catalog filament no longer exists.");
        if (materialType is not null && filament.MaterialType != materialType)
            throw new InvalidPartFilamentSelectionException($"The selected filament must use {materialType}, as required by the part file.");
    }
}
