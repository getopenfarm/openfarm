using FluentAssertions;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;
using static OpenFarm.Application.Tests.Profiles.Fakes.CatalogVersionsFactory;

namespace OpenFarm.Application.Tests.Profiles;

public class ProfileCatalogVersionsTests
{
    private static readonly SlicerRef Orca231 = new(SlicerEngine.OrcaSlicer, "2.3.1");
    private static readonly SlicerRef Orca242 = new(SlicerEngine.OrcaSlicer, "2.4.2");
    private static readonly SlicerRef Bambu271 = new(SlicerEngine.BambuStudio, "2.7.1");

    private static async Task<FakeMachineProfileRepository> IngestedAsync(params (ProfileSourceFormat Format, string Version)[] catalogs)
    {
        var machines = new FakeMachineProfileRepository();
        foreach (var (format, version) in catalogs)
        {
            await machines.AddAsync(MachineProfile.CreateSystemProfile(
                "Bambu Lab P1S 0.4 nozzle", format, null, "{}", "Bambulab", "GM014",
                isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: version), default);
        }

        return machines;
    }

    [Fact]
    public async Task GetScope_NothingIngested_HasNoCatalogForAnyFormat()
    {
        var machines = await IngestedAsync();
        var sut = Create(machines, farmDefault: null, Bundled(SlicerEngine.OrcaSlicer, "2.3.1"));

        var scope = await sut.GetScopeAsync(default);

        scope.Versions.Should().BeEmpty();
        scope.VersionFor(ProfileSourceFormat.OrcaSlicer).Should().BeNull();
        scope.VersionFor(ProfileSourceFormat.BambuStudio).Should().BeNull();
    }

    [Fact]
    public async Task GetScope_FarmDefaultIngested_BeatsANewerBundledVersion()
    {
        var machines = await IngestedAsync((ProfileSourceFormat.OrcaSlicer, "2.3.1"), (ProfileSourceFormat.OrcaSlicer, "2.4.2"));
        var sut = Create(machines, Orca231, Bundled(SlicerEngine.OrcaSlicer, "2.3.1"), Bundled(SlicerEngine.OrcaSlicer, "2.4.2"));

        var scope = await sut.GetScopeAsync(default);

        scope.VersionFor(ProfileSourceFormat.OrcaSlicer).Should().Be("2.3.1");
    }

    [Fact]
    public async Task GetScope_FormatOfAnotherEngine_UsesTheNewestBundledVersionOfItsOwnEngine()
    {
        var machines = await IngestedAsync(
            (ProfileSourceFormat.OrcaSlicer, "2.3.1"), (ProfileSourceFormat.OrcaSlicer, "2.4.2"), (ProfileSourceFormat.BambuStudio, "2.7.1"));
        var sut = Create(machines, Bambu271,
            Bundled(SlicerEngine.OrcaSlicer, "2.3.1"), Bundled(SlicerEngine.OrcaSlicer, "2.4.2"), Bundled(SlicerEngine.BambuStudio, "2.7.1"));

        var scope = await sut.GetScopeAsync(default);

        scope.VersionFor(ProfileSourceFormat.BambuStudio).Should().Be("2.7.1");
        scope.VersionFor(ProfileSourceFormat.OrcaSlicer).Should().Be("2.4.2");
    }

    [Fact]
    public async Task GetScope_BundledButNotIngested_FallsBackToTheNewestIngestedVersion()
    {
        // The presence rule: a freshly bundled slicer must not blank the pickers before its profiles are ingested.
        var machines = await IngestedAsync((ProfileSourceFormat.OrcaSlicer, "2.3.1"));
        var sut = Create(machines, Orca242, Bundled(SlicerEngine.OrcaSlicer, "2.3.1"), Bundled(SlicerEngine.OrcaSlicer, "2.4.2"));

        var scope = await sut.GetScopeAsync(default);

        scope.VersionFor(ProfileSourceFormat.OrcaSlicer).Should().Be("2.3.1");
    }

    [Fact]
    public async Task GetScope_NothingBundled_PicksTheNewestIngestedVersionByVersionOrder()
    {
        var machines = await IngestedAsync((ProfileSourceFormat.OrcaSlicer, "2.9.0"), (ProfileSourceFormat.OrcaSlicer, "2.10.0"));
        var sut = Create(machines, farmDefault: null);

        var scope = await sut.GetScopeAsync(default);

        scope.VersionFor(ProfileSourceFormat.OrcaSlicer).Should().Be("2.10.0");
    }

    [Fact]
    public async Task GetScope_VersionsOfOneFormat_DoNotLeakIntoTheOther()
    {
        var machines = await IngestedAsync((ProfileSourceFormat.BambuStudio, "2.7.1"));
        var sut = Create(machines, farmDefault: null, Bundled(SlicerEngine.OrcaSlicer, "2.3.1"));

        var scope = await sut.GetScopeAsync(default);

        scope.VersionFor(ProfileSourceFormat.BambuStudio).Should().Be("2.7.1");
        scope.VersionFor(ProfileSourceFormat.OrcaSlicer).Should().BeNull();
    }
}
