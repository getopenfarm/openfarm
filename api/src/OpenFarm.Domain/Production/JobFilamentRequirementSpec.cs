namespace OpenFarm.Domain.Production;

/// <summary>
/// One (material, color) pair a <see cref="PrintJob"/> is expected to draw from, defaulted from
/// the owning Part's own filament requirements at job creation (FR-013b). Feeds one
/// <see cref="PrintFilament"/> row per entry. At most one choice is set: a colour, a filament, a profile or
/// <see cref="AnyColor"/> (any colour of the material, any brand).
/// </summary>
public sealed record JobFilamentRequirementSpec(string? MaterialType, string? DetectedColorHex,
    Guid? NarrowFilamentVariantId = null, Guid? NarrowFilamentProfileId = null, Guid? NarrowFilamentId = null,
    bool AnyColor = false)
{
    /// <summary>An operator's choice — one colour, any colour of one filament, or any colour at all — replaces the file's detected colour.</summary>
    public bool IgnoresFileColour => NarrowFilamentVariantId is not null || NarrowFilamentId is not null || AnyColor;

    public (string? MaterialType, string? ColorHex) MatchingValues =>
        (MaterialType, IgnoresFileColour ? null : DetectedColorHex);

    /// <summary>
    /// Whether a spool of this catalog colour can print the line: the material must match, then the chosen colour, else
    /// any colour of the chosen filament, else any colour when <see cref="AnyColor"/>, else the file's colour (hex ignoring case).
    /// </summary>
    public bool Accepts(Guid variantId, Guid filamentId, string materialType, string colorHex) =>
        (MaterialType is null || MaterialType == materialType)
        && (NarrowFilamentVariantId is { } variant ? variant == variantId
            : NarrowFilamentId is { } filament ? filament == filamentId
            : AnyColor || DetectedColorHex is null || string.Equals(DetectedColorHex, colorHex, StringComparison.OrdinalIgnoreCase));
}
