namespace OpenFarm.Application.Parts;

public sealed record FilamentRequirementOverride(int RequirementIndex, Guid? NarrowFilamentVariantId, Guid? NarrowFilamentProfileId);

/// <summary>The Save action — name/tags (Add only) plus any narrow filament overrides, submitted together (research.md §2/§11).</summary>
public sealed record ConfirmPartProcessingJobCommand(
    Guid JobId,
    string? Name,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<FilamentRequirementOverride>? FilamentRequirementOverrides,
    Guid? ActingUserId);
