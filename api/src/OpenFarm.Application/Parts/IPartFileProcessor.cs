using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>
/// Milestone 1 result (research.md §3b) — nozzle diameter/material, always populated.
/// <see cref="ImmediateEstimate"/> is non-null only for a file that doesn't require slicing (one
/// atomic parse produces both milestones together). <see cref="ResolvedTargetMaterialType"/> is the
/// concrete slicing target (never the "any material" null) — only set for a requires-slicing job;
/// carried onto <see cref="PartProcessingJob.TargetMaterialType"/> since milestone 2 needs it and,
/// for an unsliced 3MF, it's only just been discovered here.
/// </summary>
public sealed record PartMaterialResolution(
    decimal NozzleDiameterMm,
    IReadOnlyList<PreviewFilamentRequirement> FilamentRequirements,
    int PlateCount,
    PartEstimateResolution? ImmediateEstimate,
    string? ResolvedTargetMaterialType = null);

/// <summary>Milestone 2 result (research.md §3b) — estimate summed across every plate, plus per-plate thumbnails.</summary>
public sealed record PartEstimateResolution(
    int EstimatedPrintTimeMinutes,
    decimal EstimatedFilamentWeightGrams,
    IReadOnlyList<ThumbnailSpec> Thumbnails);

/// <summary>
/// The single seam <c>PartProcessingBackgroundService</c> depends on — unifies the parse (already-sliced)
/// and slice (requires-slicing) paths behind one interface (research.md §2). Delegates internally to
/// <see cref="IAlreadySlicedFileMetadataParser"/>, <see cref="IUnslicedProjectMaterialResolver"/>, or
/// <see cref="ISlicerEngine"/> depending on <see cref="PartProcessingJob.RequiresSlicing"/>/
/// <see cref="PartProcessingJob.FileFormat"/>.
/// </summary>
public interface IPartFileProcessor
{
    /// <summary>
    /// Always runs first. For an already-sliced file this also fully resolves milestone 2
    /// (<see cref="PartMaterialResolution.ImmediateEstimate"/> is set); for a requires-slicing file it
    /// resolves nozzle diameter/material only — <see cref="ResolveEstimateAsync"/> continues afterward.
    /// </summary>
    /// <exception cref="PartFileParsingFailedException" />
    /// <exception cref="UnslicedProjectMaterialParsingFailedException" />
    Task<PartMaterialResolution> ResolveMaterialAsync(PartProcessingJob job, Stream fileStream, CancellationToken cancellationToken);

    /// <summary>Only called for a requires-slicing job whose milestone 1 didn't already include the estimate — invokes <see cref="ISlicerEngine"/>.</summary>
    /// <exception cref="SlicingFailedException" />
    Task<PartEstimateResolution> ResolveEstimateAsync(PartProcessingJob job, Stream fileStream, CancellationToken cancellationToken);
}
