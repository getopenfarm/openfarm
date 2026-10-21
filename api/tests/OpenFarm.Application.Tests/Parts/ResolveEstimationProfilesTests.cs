using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using static OpenFarm.Application.Tests.Profiles.Fakes.TwoEngineCatalog;

namespace OpenFarm.Application.Tests.Parts;

// Estimates use the same rule as production: the defaults the operator chose are resolved to the slicer's own profiles,
// or there is no estimate. A part pinned to a slicer that lacks a default's twin must not be estimated with another profile.
public class ResolveEstimationProfilesTests
{
    private readonly TwoEngineCatalog _catalog = new();

    private ResolveEstimationProfiles Sut()
    {
        var printers = new FakePrinterRepository();
        var compatibility = new ProfileCompatibility(_catalog.Machines, _catalog.Filaments);
        var selection = new ResolveSlicingProfileSelection(printers, _catalog.Machines, _catalog.Processes, _catalog.Filaments,
            new RepresentativeFilamentResolver(_catalog.Filaments, new FakeSpoolRepository(), new FakeFilamentRepository(), new FakeFeedUnitRepository(),
                printers, _catalog.Machines, compatibility),
            compatibility, _catalog.Resolver);
        return new ResolveEstimationProfiles(selection);
    }

    [Fact]
    public async Task DefaultsChosenInAnotherCatalog_AreEstimatedWithThePinnedSlicersOwnProfiles()
    {
        var (machine, process, filament) = _catalog.StandardSet(Orca242, Bambu271);
        var defaults = new EstimationProfileSelection(machine[Orca242].Id, process[Orca242].Id, filament[Orca242].Id, BuildPlateType.HighTemp);

        var (configuration, details) = await Sut().ResolveAsync(defaults, Bambu271, default);

        var profiles = configuration.Profiles!;
        profiles.Pin.Should().Be(Bambu271);
        profiles.MachineSettingsJson.Should().Contain(Marker("MACHINE", MachineName, Bambu271)).And.NotContain(Marker("MACHINE", MachineName, Orca242));
        profiles.ProcessSettingsJson.Should().Contain(Marker("PROCESS", "0.20mm Standard @BBL X1C", Bambu271));
        profiles.FilamentSettingsJson.Should().Contain(Marker("FILAMENT", "Bambu PLA Basic @BBL X1C", Bambu271));
        new[] { profiles.Machine, profiles.Process, profiles.Filament }.Should().OnlyContain(p => p.Outcome == ProfileResolutionOutcome.MappedEngine);
        details.Source.Should().Be("DefaultProfiles");
    }

    [Fact]
    public async Task PinnedSlicerWithoutTheDefaultProcess_HasNoEstimate_NotAnEstimateFromAnotherProfile()
    {
        var machine = _catalog.Machine(BambuVendor, MachineName, "GM014", Orca242, Bambu271);
        var process = _catalog.Process(BambuVendor, "0.20mm Standard @BBL X1C", "GP004", [MachineName], Orca242);
        var filament = _catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", [MachineName], Orca242, Bambu271);
        var defaults = new EstimationProfileSelection(machine[Orca242].Id, process[Orca242].Id, filament[Orca242].Id, BuildPlateType.HighTemp);

        var act = () => Sut().ResolveAsync(defaults, Bambu271, default);

        var blocked = (await act.Should().ThrowAsync<ProfileSetBlockedException>()).Which;
        blocked.Code.Should().Be(nameof(ProfileResolutionOutcome.Missing));
        blocked.Message.Should().Contain("Process profile '0.20mm Standard @BBL X1C' is not in Bambu Studio 2.7.1");
    }

    [Fact]
    public async Task PinnedCatalogNeverIngested_HasNoEstimate()
    {
        var (machine, process, filament) = _catalog.StandardSet(Orca242);
        var neverIngested = new SlicerRef(SlicerEngine.BambuStudio, "9.9.9");
        var defaults = new EstimationProfileSelection(machine[Orca242].Id, process[Orca242].Id, filament[Orca242].Id, BuildPlateType.HighTemp);

        var act = () => Sut().ResolveAsync(defaults, neverIngested, default);

        var blocked = (await act.Should().ThrowAsync<ProfileSetBlockedException>()).Which;
        blocked.Code.Should().Be(nameof(ProfileResolutionOutcome.CatalogNotLoaded));
        blocked.Message.Should().Contain("Bambu Studio 9.9.9 profiles are not loaded");
    }
}
