using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Tests.Parts.Fakes;

public sealed class FakeHostPlatformProvider(SlicerPlatform current) : IHostPlatformProvider
{
    public SlicerPlatform Current { get; } = current;
}
