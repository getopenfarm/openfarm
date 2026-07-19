using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>One detected material/color slot, from one plate/AMS-slot, before grouping (research.md §7).</summary>
public sealed record DetectedFilamentSlot(string MaterialType, string? DetectedColorHex, string? DetectedSubBrand);

/// <summary>
/// The full atomic parse result of an already-sliced file — nozzle diameter, a summed estimate
/// across every plate, per-plate thumbnails, and every detected filament slot across every plate/AMS
/// slot (pre-grouping — the caller runs <c>FilamentRequirementGrouper</c>) (research.md §1/§7/§8).
/// </summary>
public sealed record SlicedFileMetadata(
    decimal NozzleDiameterMm,
    int EstimatedPrintTimeMinutes,
    decimal EstimatedFilamentWeightGrams,
    int PlateCount,
    IReadOnlyList<DetectedFilamentSlot> DetectedFilamentSlots,
    IReadOnlyList<ThumbnailSpec> Thumbnails);

/// <summary>
/// Parses an already-sliced file's own embedded metadata (G-code header comments, or a
/// <c>.gcode.3mf</c>'s per-plate embedded slice-result data) — never invoked for a file requiring
/// slicing (research.md §1).
/// </summary>
public interface IAlreadySlicedFileMetadataParser
{
    /// <exception cref="PartFileParsingFailedException">The file's metadata could not be parsed (FR-006c).</exception>
    Task<SlicedFileMetadata> ParseAsync(Stream fileStream, PartFileFormat format, CancellationToken cancellationToken);
}
