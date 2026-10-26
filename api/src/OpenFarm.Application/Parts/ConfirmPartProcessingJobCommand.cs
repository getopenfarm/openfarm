namespace OpenFarm.Application.Parts;

public sealed record FilamentRequirementOverride(int RequirementIndex, Guid? NarrowFilamentVariantId, Guid? NarrowFilamentProfileId);

/// <summary>
/// The Save action — name/tags (Add only) plus any narrow filament overrides, submitted together (research.md §2/§11).
/// <paramref name="PlateNumbers"/> is the plate the part prints; it may be left out when the file offers one plate. Adding a
/// file can name several: one part each ("name – plate name"), and with <paramref name="CreateAssembly"/> an assembly of them.
/// </summary>
public sealed record ConfirmPartProcessingJobCommand(
    Guid JobId,
    string? Name,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<FilamentRequirementOverride>? FilamentRequirementOverrides,
    Guid? ActingUserId,
    IReadOnlyList<int>? PlateNumbers = null,
    bool CreateAssembly = false);
