using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>
/// Result of an in-app slice — estimate summed across every plate, plus per-plate thumbnails
/// (research.md §4/§8). <see cref="OutputFileBytes"/> is the actual sliced output (014 FR-011,
/// job-dispatch-time re-slicing) — every producer populates it, but only dispatch-time callers read
/// it; the upload-time estimate pipeline (009) discards it same as before. <see cref="OutputFileFormat"/>
/// distinguishes a retained sliced 3MF archive from bare G-code.
/// </summary>
public sealed record SlicingResult(
    int PlateCount,
    int? EstimatedPrintTimeMinutes,
    decimal? EstimatedFilamentWeightGrams,
    IReadOnlyList<ThumbnailSpec> Thumbnails,
    byte[] OutputFileBytes,
    PartFileFormat OutputFileFormat = PartFileFormat.GCode);

/// <summary>
/// Invokes the configured headless slicer CLI. Reusable, unmodified, by Phase 7's future
/// job-dispatch-time slicing (FR-010) — this phase's own pipeline is just one consumer.
/// <paramref name="materialType"/> is always a concrete, already-resolved value (the fixed STL
/// fallback profile or an unsliced 3MF's own parsed material) — this interface never sees "any
/// material" itself (research.md §7a).
/// </summary>
public interface ISlicerEngine
{
    /// <summary>Library estimation consumes explicit immutable settings, never fleet selection.</summary>
    Task<SlicingResult> EstimateAsync(SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat,
        EstimationConfiguration configuration, CancellationToken cancellationToken) =>
        throw new SlicingFailedException("The selected slicer does not support file-based estimation.");

    Task<SlicingResult> SliceConfiguredAsync(SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat,
        string materialType, decimal nozzleDiameterMm, Guid printerId, ResolvedSlicingProfiles profiles, CancellationToken cancellationToken)
        => SliceAsync(slicer, modelFileStream, modelFileFormat, materialType, nozzleDiameterMm, printerId, cancellationToken);

    /// <param name="printerId">The assigned production printer. Library estimates use EstimateAsync.</param>
    /// <exception cref="SlicingFailedException">The CLI process failed, timed out, or produced unparseable output.</exception>
    /// <exception cref="SlicerUnavailableException"><paramref name="slicer"/> is not bundled for the host platform.</exception>
    Task<SlicingResult> SliceAsync(
        SlicerRef slicer,
        Stream modelFileStream,
        PartFileFormat modelFileFormat,
        string materialType,
        decimal nozzleDiameterMm,
        Guid? printerId,
        CancellationToken cancellationToken);
}
