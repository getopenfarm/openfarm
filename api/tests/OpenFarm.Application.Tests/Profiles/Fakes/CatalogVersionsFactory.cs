using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Parts.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Application.Tests.Profiles.Fakes;

/// <summary>A real <see cref="ProfileCatalogVersions"/> over the fake machine repository, with a Linux host and the given bundled slicers.</summary>
public static class CatalogVersionsFactory
{
    public static ProfileCatalogVersions Create(FakeMachineProfileRepository machines, SlicerRef? farmDefault, params AvailableSlicer[] bundled)
    {
        var settings = FarmSettings.Create();
        if (farmDefault is not null)
        {
            settings.SetDefaultSlicer(farmDefault);
        }

        return new ProfileCatalogVersions(
            new FakeFarmSettingsRepository(settings),
            new ResolveSlicerSelection(new FakeAvailableSlicerCatalog(bundled), new FakeHostPlatformProvider(SlicerPlatform.Linux)),
            machines);
    }

    public static AvailableSlicer Bundled(SlicerEngine engine, string version) =>
        new(engine, version, SlicerPlatform.Linux, $"/bundle/{engine}/{version}/linux/slicer");
}
