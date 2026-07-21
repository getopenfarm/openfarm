using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>
/// Eager-initialized singleton registry (research.md §2) — runs <see cref="BundledSlicerScanner"/>
/// once at construction (i.e. during application startup, before the host starts serving requests),
/// and exposes an immutable snapshot for the rest of the process's lifetime. No runtime rescan: the
/// bundle is fixed for a running install.
/// </summary>
public sealed class AvailableSlicerCatalog : IAvailableSlicerCatalog
{
    private readonly IReadOnlyList<AvailableSlicer> _available;

    public AvailableSlicerCatalog(BundledSlicerScanner scanner)
    {
        _available = scanner.Scan();
    }

    public IReadOnlyList<AvailableSlicer> GetAvailable() => _available;

    public AvailableSlicer? Find(SlicerRef slicerRef, SlicerPlatform platform) =>
        _available.FirstOrDefault(a =>
            a.Engine == slicerRef.Engine
            && string.Equals(a.Version, slicerRef.Version, StringComparison.Ordinal)
            && a.Platform == platform);
}
