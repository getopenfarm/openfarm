using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts.RealSlicer;

/// <summary>
/// A <see cref="FactAttribute"/> that self-skips when the requested (engine, version) isn't actually
/// installed on this machine. xUnit 2.9.3 has no built-in dynamic skip; setting the inherited
/// <c>Skip</c> property here works because attribute construction happens at test discovery time,
/// late enough to check the filesystem.
/// </summary>
public sealed class RealSlicerFactAttribute : FactAttribute
{
    public RealSlicerFactAttribute(SlicerEngine engine, string version)
    {
        var (available, reason) = RealSlicerBundleLocator.TryResolve(engine, version);
        if (available is null)
        {
            Skip = reason;
        }
    }
}
