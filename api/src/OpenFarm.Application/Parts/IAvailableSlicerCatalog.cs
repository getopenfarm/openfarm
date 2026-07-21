using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>
/// The discovered-slicer registry (research.md §1/§2) — backed by a startup filesystem scan of the
/// bundled slicer directory (<c>Infrastructure.Parts.BundledSlicerScanner</c>), eagerly populated
/// once and never rescanned for the lifetime of the process. Presence in <see cref="GetAvailable"/>
/// is availability; there is no execution/validation step (FR-002).
/// </summary>
public interface IAvailableSlicerCatalog
{
    /// <summary>Every discovered <see cref="AvailableSlicer"/>, host-platform-usable entries only.</summary>
    IReadOnlyList<AvailableSlicer> GetAvailable();

    /// <summary>Looks up one exact <paramref name="slicerRef"/> for <paramref name="platform"/>; <c>null</c> when not bundled.</summary>
    AvailableSlicer? Find(SlicerRef slicerRef, SlicerPlatform platform);
}
