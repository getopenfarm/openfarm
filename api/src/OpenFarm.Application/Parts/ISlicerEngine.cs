using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>Result of an in-app slice — estimate summed across every plate, plus per-plate thumbnails (research.md §4/§8).</summary>
public sealed record SlicingResult(
    int PlateCount,
    int EstimatedPrintTimeMinutes,
    decimal EstimatedFilamentWeightGrams,
    IReadOnlyList<ThumbnailSpec> Thumbnails);

/// <summary>
/// Invokes the configured headless slicer CLI. Reusable, unmodified, by Phase 7's future
/// job-dispatch-time slicing (FR-010) — this phase's own pipeline is just one consumer.
/// <paramref name="materialType"/> is always a concrete, already-resolved value (the fixed STL
/// fallback profile or an unsliced 3MF's own parsed material) — this interface never sees "any
/// material" itself (research.md §7a).
/// </summary>
public interface ISlicerEngine
{
    /// <exception cref="SlicingFailedException">The CLI process failed, timed out, or produced unparseable output.</exception>
    Task<SlicingResult> SliceAsync(
        SlicerEngine engine,
        Stream modelFileStream,
        string materialType,
        decimal nozzleDiameterMm,
        CancellationToken cancellationToken);
}
