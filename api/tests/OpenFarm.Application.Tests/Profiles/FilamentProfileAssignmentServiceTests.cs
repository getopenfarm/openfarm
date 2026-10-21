using FluentAssertions;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using static OpenFarm.Application.Tests.Profiles.Fakes.TwoEngineCatalog;

namespace OpenFarm.Application.Tests.Profiles;

// A filament's assignment is the preset a new part (pinned to the farm default slicer) would slice with: it must
// resolve to that slicer's own catalog, or it is not an assignment the farm can use.
public class FilamentProfileAssignmentServiceTests
{
    private static FilamentProfileAssignmentService ServiceOver(TwoEngineCatalog catalog, SlicerRef farmDefault) =>
        new(catalog.Filaments, new FakePrinterRepository(), new ProfileCompatibility(catalog.Machines, catalog.Filaments),
            new FakeSlicingDefaultsQuery(farmDefault.Engine, farmDefault.Version), catalog.Resolver);

    private static Filament NewFilament() => Filament.Create(Guid.NewGuid(), "PLA Basic", "PLA");

    private static FilamentProfileAssignmentInput Assign(Guid profileId) => new("Bambu", "P1S", 0.4m, profileId);

    private static (TwoEngineCatalog Catalog, IReadOnlyDictionary<SlicerRef, OpenFarm.Domain.Profiles.FilamentProfile> Filament) Catalog(params SlicerRef[] filamentIn)
    {
        var catalog = new TwoEngineCatalog();
        catalog.Machine(TwoEngineCatalog.BambuVendor, MachineName, "GM014", Orca242, Bambu271);
        var filament = catalog.Filament(TwoEngineCatalog.BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", [MachineName], filamentIn);
        return (catalog, filament);
    }

    [Fact]
    public async Task Replace_PresetOfTheDefaultSlicersOwnCatalog_IsKept()
    {
        var (catalog, filament) = Catalog(Orca242, Bambu271);
        var assigned = NewFilament();

        await ServiceOver(catalog, Orca242).ReplaceAsync(assigned, [Assign(filament[Orca242].Id)], default);

        assigned.ProfileAssignments.Should().ContainSingle().Which.FilamentProfileId.Should().Be(filament[Orca242].Id);
    }

    [Fact]
    public async Task Replace_OtherEnginesPresetWithATwinInTheDefaultSlicersCatalog_IsKept()
    {
        var (catalog, filament) = Catalog(Orca242, Bambu271);
        var assigned = NewFilament();

        await ServiceOver(catalog, Orca242).ReplaceAsync(assigned, [Assign(filament[Bambu271].Id)], default);

        assigned.ProfileAssignments.Should().ContainSingle();
    }

    [Fact]
    public async Task Replace_OtherEnginesPresetWithNoTwin_IsRefusedAndNothingIsSaved()
    {
        var (catalog, filament) = Catalog(Bambu271);
        catalog.Loaded(Orca242);
        var assigned = NewFilament();

        var act = () => ServiceOver(catalog, Orca242).ReplaceAsync(assigned, [Assign(filament[Bambu271].Id)], default);

        (await act.Should().ThrowAsync<OpenFarm.Domain.Parts.SlicingProfileUnconfiguredException>())
            .Which.Message.Should().Contain("is not in OrcaSlicer 2.4.2");
        assigned.ProfileAssignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Replace_ImportedPresetOfTheOtherEnginesFormat_IsRefused()
    {
        var (catalog, _) = Catalog(Orca242);
        var imported = OpenFarm.Domain.Profiles.FilamentProfile.Create("Imported Bambu PLA", OpenFarm.Domain.Profiles.ProfileSourceFormat.BambuStudio, null,
            """{"filament_type":["PLA"]}""");
        catalog.Filaments.Stage(imported);

        var act = () => ServiceOver(catalog, Orca242).ReplaceAsync(NewFilament(), [Assign(imported.Id)], default);

        (await act.Should().ThrowAsync<OpenFarm.Domain.Parts.SlicingProfileUnconfiguredException>()).Which.Code.Should().Be("UnsupportedOrigin");
    }

    [Fact]
    public async Task GetIssues_ReportsAnAssignmentTheDefaultSlicerCannotResolve()
    {
        var (catalog, filament) = Catalog(Orca242, Bambu271);
        var assigned = NewFilament();
        await ServiceOver(catalog, Orca242).ReplaceAsync(assigned, [Assign(filament[Orca242].Id)], default);

        // The farm default moves to a slicer version whose catalog has no such preset.
        catalog.Loaded(new SlicerRef(SlicerEngine.OrcaSlicer, "2.5.0"));
        var issues = await ServiceOver(catalog, new SlicerRef(SlicerEngine.OrcaSlicer, "2.5.0")).GetIssuesAsync(assigned, default);

        issues.Should().ContainSingle().Which.Message.Should().Contain("is not in OrcaSlicer 2.5.0");
    }
}
