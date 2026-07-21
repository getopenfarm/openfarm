using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>Shared "available[]" projection (engine, then version descending) used by both the GET and PUT slicing-defaults responses (contracts/slicer-management-api.md).</summary>
public static class AvailableSlicerListing
{
    private static readonly SlicerVersionComparer VersionComparer = new();

    public static IReadOnlyList<AvailableSlicerSummary> Build(IAvailableSlicerCatalog catalog, SlicerPlatform platform) =>
        catalog.GetAvailable()
            .Where(a => a.Platform == platform)
            .OrderBy(a => a.Engine)
            .ThenByDescending(a => a.Version, VersionComparer)
            .Select(a => new AvailableSlicerSummary(a.Engine, a.Version, a.Platform))
            .ToList();
}
