namespace OpenFarm.Domain.Parts;

/// <summary>
/// A <see cref="PartProcessingJob"/>'s preview copy of one grouped filament requirement — stored as
/// part of the job's <c>jsonb</c> <c>PreviewFilamentRequirements</c> column, not a child table
/// (research.md §3a). <see cref="RequirementIndex"/> is a stable 0-based, first-seen-order key the
/// Confirm request's narrow-override entries reference.
/// </summary>
public sealed record PreviewFilamentRequirement(
    int RequirementIndex,
    string? MaterialType,
    string? DetectedColorHex,
    string? DetectedSubBrand,
    Guid? NarrowFilamentVariantId = null,
    Guid? NarrowFilamentProfileId = null,
    decimal? EstimatedGrams = null)
{
    /// <summary>Returns <paramref name="requirements"/> with grams from <see cref="FilamentGrams.Distribute"/>; null when none resolve.</summary>
    public static IReadOnlyList<PreviewFilamentRequirement> WithGrams(
        IReadOnlyList<PreviewFilamentRequirement> requirements, IReadOnlyList<FilamentGrams>? grams)
    {
        var split = FilamentGrams.Distribute(requirements.Select(r => (r.MaterialType, r.DetectedColorHex)).ToList(), grams);
        return requirements.Select((r, i) => r with { EstimatedGrams = split[i] }).ToList();
    }
}

/// <summary>
/// One plate a <see cref="PartProcessingJob"/> offers to become the part (a part prints one plate). Its filaments are the
/// job's <see cref="PreviewFilamentRequirement"/>s at <see cref="RequirementIndices"/>; its estimate and per-colour
/// <see cref="FilamentGrams"/> are that plate's alone. Stored in the job's <c>jsonb</c> <c>PreviewPlatesJson</c>.
/// </summary>
public sealed record PreviewPlate(
    int Number,
    string? Name,
    IReadOnlyList<int> RequirementIndices,
    int? EstimatedPrintTimeMinutes = null,
    decimal? EstimatedFilamentWeightGrams = null,
    IReadOnlyList<FilamentGrams>? FilamentGrams = null);
