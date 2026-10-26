using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>
/// An unsliced 3MF's own project-level filament assignment plus its plates with objects on them, each with the slots it
/// prints (research.md §13) — never invoked for an STL (which always takes the fixed fallback instead, research.md §7a)
/// or an already-sliced file.
/// </summary>
public sealed record UnslicedProjectMaterial(string? MaterialType, IReadOnlyList<FilePlate> Plates,
    IReadOnlyList<ThumbnailSpec>? Thumbnails = null, bool HasEmbeddedSettings = false,
    decimal? NozzleDiameterMm = null, EstimationDetails? Estimation = null,
    string? EstimationUnavailableReason = null, IReadOnlyList<DetectedFilamentSlot>? FilamentSlots = null,
    bool MaterialMetadataComplete = true);

public interface IUnslicedProjectMaterialResolver
{
    /// <exception cref="OpenFarm.Domain.Parts.UnslicedProjectMaterialParsingFailedException">The project's own filament-preset assignment could not be parsed (FR-006).</exception>
    Task<UnslicedProjectMaterial> ResolveAsync(Stream fileStream, CancellationToken cancellationToken);
}
