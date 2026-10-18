namespace OpenFarm.Domain.Production;

/// <summary>
/// One (material, color) pair a <see cref="PrintJob"/> is expected to draw from, defaulted from
/// the owning Part's own filament requirements at job creation (FR-013b). Feeds one
/// <see cref="PrintFilament"/> row per entry.
/// </summary>
public sealed record JobFilamentRequirementSpec(string? MaterialType, string? DetectedColorHex,
    Guid? NarrowFilamentVariantId = null, Guid? NarrowFilamentProfileId = null)
{
    // An operator's catalog choice takes precedence over the file's detected colour.
    public (string? MaterialType, string? ColorHex) MatchingValues =>
        (MaterialType, NarrowFilamentVariantId is null ? DetectedColorHex : null);
}
