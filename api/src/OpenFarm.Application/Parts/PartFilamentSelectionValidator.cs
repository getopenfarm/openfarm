using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Application.Parts;

public sealed class InvalidPartFilamentSelectionException(string message) : Exception(message);

public sealed class PartFilamentSelectionValidator(IFilamentRepository filaments)
{
    /// <summary>
    /// The one check every filament choice passes (confirm, part edit, job edit): at most one of one colour, any colour of
    /// one filament, one profile or any colour at all; a chosen filament or colour exists and uses the required material.
    /// </summary>
    public async Task ValidateAsync(string? materialType, Guid? variantId, CancellationToken ct, Guid? filamentId = null,
        Guid? profileId = null, bool anyColor = false)
    {
        if ((variantId is null ? 0 : 1) + (profileId is null ? 0 : 1) + (filamentId is null ? 0 : 1) + (anyColor ? 1 : 0) > 1)
            throw new InvalidPartFilamentSelectionException(
                "Set at most one of narrowFilamentVariantId, narrowFilamentId, narrowFilamentProfileId and anyColor per requirement.");
        if (filamentId is { } anyColourOf)
        {
            var chosen = await filaments.GetByIdAsync(anyColourOf, ct)
                ?? throw new InvalidPartFilamentSelectionException("The selected catalog filament no longer exists.");
            RequireMaterial(materialType, chosen.MaterialType);
        }
        if (variantId is null) return;
        var variant = await filaments.FindVariantByIdAsync(variantId.Value, ct)
            ?? throw new InvalidPartFilamentSelectionException("The selected catalog filament colour variant no longer exists.");
        var filament = await filaments.GetByIdAsync(variant.FilamentId, ct)
            ?? throw new InvalidPartFilamentSelectionException("The selected catalog filament no longer exists.");
        RequireMaterial(materialType, filament.MaterialType);
    }

    private static void RequireMaterial(string? required, string chosen)
    {
        if (required is not null && chosen != required)
            throw new InvalidPartFilamentSelectionException($"The selected filament must use {required}, as required by the part file.");
    }
}
