using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Parts;
using static OpenFarm.Application.Tests.Profiles.Fakes.TwoEngineCatalog;

namespace OpenFarm.Application.Tests.Parts;

// The presets a project saved, found by name in the pinned slicer's catalog — or no estimate, never another preset.
public class ResolveProjectEstimationProfilesTests
{
    private readonly TwoEngineCatalog _catalog = new();

    private static EstimationDetails Project(string filament = "Bambu PLA Basic @BBL X1C", string plate = "High Temp Plate",
        string machine = MachineName, params string[] moreFilaments) =>
        new("EmbeddedProject", "Bambu Lab P1S", machine, "0.20mm Standard @BBL X1C", [filament, .. moreFilaments], plate);

    private ResolveProjectEstimationProfiles Sut()
    {
        var printers = new FakePrinterRepository();
        var compatibility = new ProfileCompatibility(_catalog.Machines, _catalog.Filaments);
        var selection = new ResolveSlicingProfileSelection(printers, _catalog.Machines, _catalog.Processes, _catalog.Filaments,
            new RepresentativeFilamentResolver(_catalog.Filaments, new FakeSpoolRepository(), new FakeFilamentRepository(), new FakeFeedUnitRepository(),
                printers, _catalog.Machines, compatibility),
            compatibility, _catalog.Resolver);
        return new ResolveProjectEstimationProfiles(_catalog.Machines, _catalog.Processes, _catalog.Filaments, new ResolveEstimationProfiles(selection));
    }

    [Fact]
    public async Task PresetsNamedByTheProject_AreResolvedInThePinnedCatalog()
    {
        _catalog.StandardSet(Orca231);

        var (configuration, details) = await Sut().ResolveAsync(Project(), Orca231, default);

        configuration.EmbeddedProject.Should().BeFalse();
        configuration.Profiles!.Pin.Should().Be(Orca231);
        configuration.Profiles.MachineSettingsJson.Should().Contain(Marker("MACHINE", MachineName, Orca231));
        details.Source.Should().Be("ProjectPresets");
        details.Engine.Should().Be("OrcaSlicer");
        details.Version.Should().Be("2.3.1");
        details.BuildPlate.Should().Be("High Temp Plate");
    }

    [Fact]
    public async Task SeveralSlotsSharingOnePreset_AreOneFilament()
    {
        _catalog.StandardSet(Orca231);

        var (_, details) = await Sut().ResolveAsync(Project(moreFilaments: "Bambu PLA Basic @BBL X1C"), Orca231, default);

        details.FilamentProfileNames.Should().Equal("Bambu PLA Basic @BBL X1C");
    }

    [Fact]
    public async Task PresetNotInThePinnedCatalog_HasNoEstimate()
    {
        _catalog.StandardSet(Orca242);
        _catalog.Loaded(Orca231);

        var act = () => Sut().ResolveAsync(Project(), Orca231, default);

        (await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>())
            .Which.Message.Should().Contain("printer preset 'Bambu Lab P1S 0.4 nozzle' is not in the OrcaSlicer 2.3.1 catalog");
    }

    [Fact]
    public async Task SeveralDifferentFilamentPresets_HaveNoEstimate()
    {
        _catalog.StandardSet(Orca231);

        var act = () => Sut().ResolveAsync(Project(moreFilaments: "Bambu PETG Basic @BBL X1C"), Orca231, default);

        (await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>()).Which.Message.Should().Contain("several filament presets");
    }

    [Fact]
    public async Task UnknownBuildPlate_HasNoEstimate()
    {
        _catalog.StandardSet(Orca231);

        var act = () => Sut().ResolveAsync(Project(plate: "Mystery Plate"), Orca231, default);

        (await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>()).Which.Message.Should().Contain("build plate 'Mystery Plate'");
    }
}
