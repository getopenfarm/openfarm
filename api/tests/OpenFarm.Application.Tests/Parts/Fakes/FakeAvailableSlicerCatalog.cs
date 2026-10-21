using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Tests.Parts.Fakes;

public sealed class FakeAvailableSlicerCatalog(IReadOnlyList<AvailableSlicer> available) : IAvailableSlicerCatalog
{
    public IReadOnlyList<AvailableSlicer> GetAvailable() => available;

    public AvailableSlicer? Find(SlicerRef slicerRef, SlicerPlatform platform) =>
        available.FirstOrDefault(a =>
            a.Engine == slicerRef.Engine
            && string.Equals(a.Version, slicerRef.Version, StringComparison.Ordinal)
            && a.Platform == platform);
}
