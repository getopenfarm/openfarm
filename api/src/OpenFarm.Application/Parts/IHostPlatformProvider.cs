using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>
/// The running host's platform, as a small Application-facing seam over
/// <c>Infrastructure.Parts.HostPlatform</c>'s <c>RuntimeInformation.IsOSPlatform</c> resolution
/// (research.md §4/§9) — keeps <c>ResolveSlicerSelection</c> from depending on Infrastructure
/// directly, and lets tests substitute a fixed platform.
/// </summary>
public interface IHostPlatformProvider
{
    SlicerPlatform Current { get; }
}
