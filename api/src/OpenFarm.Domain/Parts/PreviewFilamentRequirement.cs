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
    Guid? NarrowFilamentProfileId = null);
