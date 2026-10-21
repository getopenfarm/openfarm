using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;
using static OpenFarm.Application.Tests.Profiles.Fakes.TwoEngineCatalog;

namespace OpenFarm.Application.Tests.Parts;

// A printer (or the farm's estimation defaults) keeps pointing at whatever preset row was picked — in another slicer
// version's catalog, or in the other engine's. The slice must use the data of the slicer it is pinned to, or not
// happen at all. Row markers prove which catalog's data flowed into the flattened settings.
public class ResolveSlicingProfileVersionMappingTests
{
    private static ResolveSlicingProfileSelection SelectionOver(TwoEngineCatalog catalog)
    {
        var printers = new FakePrinterRepository();
        var compatibility = new ProfileCompatibility(catalog.Machines, catalog.Filaments);
        return new ResolveSlicingProfileSelection(
            printers, catalog.Machines, catalog.Processes, catalog.Filaments,
            new RepresentativeFilamentResolver(catalog.Filaments, new FakeSpoolRepository(), new FakeFilamentRepository(), new FakeFeedUnitRepository(), printers, catalog.Machines, compatibility),
            compatibility, catalog.Resolver);
    }

    private static Task<ResolvedSlicingProfiles> ResolveAsync(
        ResolveSlicingProfileSelection selection, MachineProfile machine, ProcessProfile process, FilamentProfile filament,
        SlicerRef pin, ProfileUseContext? use = null) =>
        selection.ResolveProfilesAsync(machine.Id, process.Id, filament.Id, BuildPlateType.HighTemp, pin, use, default);

    private static readonly ProfileUseContext P1S = new("Bambu", "P1S", 0.4m, "PLA");

    // ---------------------------------------------------------------- same engine, other version

    [Fact]
    public async Task PresetsStoredInAnOlderVersion_FlattenWithThePinnedVersionsData()
    {
        var catalog = new TwoEngineCatalog();
        var (machine, process, filament) = catalog.StandardSet(Orca231, Orca242);

        var result = await ResolveAsync(SelectionOver(catalog), machine[Orca231], process[Orca231], filament[Orca231], Orca242);

        result.MachineSettingsJson.Should().Contain(Marker("MACHINE", MachineName, Orca242)).And.NotContain("2.3.1");
        result.ProcessSettingsJson.Should().Contain(Marker("PROCESS", "0.20mm Standard @BBL X1C", Orca242)).And.NotContain("2.3.1");
        result.FilamentSettingsJson.Should().Contain(Marker("FILAMENT", "Bambu PLA Basic @BBL X1C", Orca242)).And.NotContain("2.3.1");
        result.Pin.Should().Be(Orca242);
        result.Filament.Outcome.Should().Be(ProfileResolutionOutcome.MappedVersion);
        result.Filament.SelectedId.Should().Be(filament[Orca231].Id);
        result.Filament.ResolvedId.Should().Be(filament[Orca242].Id);
    }

    [Fact]
    public async Task PinnedToTheStoredVersion_UsesTheStoredRows()
    {
        var catalog = new TwoEngineCatalog();
        var (machine, process, filament) = catalog.StandardSet(Orca231, Orca242);

        var result = await ResolveAsync(SelectionOver(catalog), machine[Orca231], process[Orca231], filament[Orca231], Orca231);

        result.FilamentSettingsJson.Should().Contain(Marker("FILAMENT", "Bambu PLA Basic @BBL X1C", Orca231)).And.NotContain("2.4.2");
        result.Machine.Outcome.Should().Be(ProfileResolutionOutcome.Exact);
        result.Process.Outcome.Should().Be(ProfileResolutionOutcome.Exact);
        result.Filament.Outcome.Should().Be(ProfileResolutionOutcome.Exact);
    }

    [Fact]
    public async Task UserPresetOnAnOldBase_InheritsFromThePinnedVersionsBaseAndKeepsItsOverrides()
    {
        var catalog = new TwoEngineCatalog();
        var (machine, process, _) = catalog.StandardSet(Orca231, Orca242);
        FilamentProfile Base(SlicerRef version) => FilamentProfile.CreateSystemProfile("PLA @base", FormatOf(version), null,
            $$"""{"type":"filament","filament_type":["PLA"],"hot_plate_temp":["55"],"marker":"BASE-{{version.Version}}"}""",
            BambuVendor, null, isSelectable: false, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: version.Version);
        var base231 = Base(Orca231);
        var base242 = Base(Orca242);
        catalog.Filaments.Stage(base231);
        catalog.Filaments.Stage(base242);
        var user = FilamentProfile.Create("My PLA", ProfileSourceFormat.OrcaSlicer, base231.Name, """{"user":"USER-OVERRIDE"}""");
        user.ResolveBaseProfile(base231.Id);
        catalog.Filaments.Stage(user);

        var result = await ResolveAsync(SelectionOver(catalog), machine[Orca231], process[Orca231], user, Orca242);

        result.FilamentSettingsJson.Should().Contain("BASE-2.4.2").And.Contain("USER-OVERRIDE").And.NotContain("BASE-2.3.1");
        result.Filament.Outcome.Should().Be(ProfileResolutionOutcome.UserNative);
    }

    [Fact]
    public async Task UserPresetWhoseBaseIsMissingInThePinnedVersion_IsBlocked()
    {
        var catalog = new TwoEngineCatalog();
        var (machine, process, _) = catalog.StandardSet(Orca231, Orca242);
        var base231 = FilamentProfile.CreateSystemProfile("PLA @base", ProfileSourceFormat.OrcaSlicer, null,
            """{"filament_type":["PLA"],"hot_plate_temp":["55"]}""", BambuVendor, null, isSelectable: false, compatibleMachineNames: null,
            materialType: "PLA", includedTemplateNames: null, sourceVersion: "2.3.1");
        catalog.Filaments.Stage(base231);
        var user = FilamentProfile.Create("My PLA", ProfileSourceFormat.OrcaSlicer, base231.Name, "{}");
        user.ResolveBaseProfile(base231.Id);
        catalog.Filaments.Stage(user);

        var act = () => ResolveAsync(SelectionOver(catalog), machine[Orca231], process[Orca231], user, Orca242);

        (await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>()).Which.Message.Should().Contain("'PLA @base' is not in OrcaSlicer 2.4.2");
    }

    // ---------------------------------------------------------------- fail closed

    [Fact]
    public async Task PinnedVersionLacksAPreset_BlocksAndNamesTheMissingProfile()
    {
        var catalog = new TwoEngineCatalog();
        var (machine, process, filament) = catalog.StandardSet(Orca231);
        catalog.Machine(BambuVendor, MachineName, "GM014", Orca242);
        catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", [MachineName], Orca242);

        var act = () => ResolveAsync(SelectionOver(catalog), machine[Orca231], process[Orca231], filament[Orca231], Orca242);

        var failure = await act.Should().ThrowAsync<ProfileSetBlockedException>();
        failure.Which.Code.Should().Be(nameof(ProfileResolutionOutcome.Missing));
        failure.Which.Message.Should().Contain("Process profile '0.20mm Standard @BBL X1C' is not in OrcaSlicer 2.4.2");
        failure.Which.Issues.Should().ContainSingle().Which.Kind.Should().Be(ProfileKind.Process);
    }

    [Fact]
    public async Task PinnedCatalogNeverIngested_Blocks_AndDoesNotFallBackToTheStoredRows()
    {
        var catalog = new TwoEngineCatalog();
        var (machine, process, filament) = catalog.StandardSet(Orca231);

        var act = () => ResolveAsync(SelectionOver(catalog), machine[Orca231], process[Orca231], filament[Orca231], new SlicerRef(SlicerEngine.OrcaSlicer, "9.9.9"));

        var failure = await act.Should().ThrowAsync<ProfileSetBlockedException>();
        failure.Which.Code.Should().Be(nameof(ProfileResolutionOutcome.CatalogNotLoaded));
        failure.Which.Issues.Should().HaveCount(3);
        failure.Which.Message.Should().Be("OrcaSlicer 9.9.9 profiles are not loaded. Restart the app or run ingest-system-profiles.");
    }

    [Fact]
    public async Task EveryBlockedPresetIsReportedAtOnce()
    {
        var catalog = new TwoEngineCatalog();
        var (machine, process, filament) = catalog.StandardSet(Orca231);
        catalog.Loaded(Bambu271);

        var set = await SelectionOver(catalog).ResolveSetAsync(machine[Orca231].Id, process[Orca231].Id, filament[Orca231].Id, Bambu271, default);

        set.Set.Should().BeNull();
        set.Issues.Select(i => (i.Kind, i.Code)).Should().BeEquivalentTo(new[]
        {
            (ProfileKind.Machine, ProfileResolutionOutcome.Missing),
            (ProfileKind.Process, ProfileResolutionOutcome.Missing),
            (ProfileKind.Filament, ProfileResolutionOutcome.Missing),
        });
    }

    [Fact]
    public async Task DeletedSelection_IsReportedAsInvalid()
    {
        var catalog = new TwoEngineCatalog();
        var (machine, process, _) = catalog.StandardSet(Orca231);

        var set = await SelectionOver(catalog).ResolveSetAsync(machine[Orca231].Id, process[Orca231].Id, Guid.NewGuid(), Orca231, default);

        set.Issues.Should().ContainSingle().Which.Should().Match<ProfileIssue>(i =>
            i.Kind == ProfileKind.Filament && i.Code == ProfileResolutionOutcome.InvalidRow && i.Message == "The selected filament profile no longer exists.");
    }

    // ---------------------------------------------------------------- the other engine's twin

    [Fact]
    public async Task OrcaSelection_PinnedToBambuStudio_FlattensBambuStudiosOwnTwins()
    {
        var catalog = new TwoEngineCatalog();
        var (machine, process, filament) = catalog.StandardSet(Orca231, Bambu271);

        var result = await ResolveAsync(SelectionOver(catalog), machine[Orca231], process[Orca231], filament[Orca231], Bambu271, P1S);

        result.MachineSettingsJson.Should().Contain(Marker("MACHINE", MachineName, Bambu271)).And.NotContain("OrcaSlicer");
        result.ProcessSettingsJson.Should().Contain(Marker("PROCESS", "0.20mm Standard @BBL X1C", Bambu271)).And.NotContain("OrcaSlicer");
        result.FilamentSettingsJson.Should().Contain(Marker("FILAMENT", "Bambu PLA Basic @BBL X1C", Bambu271)).And.NotContain("OrcaSlicer");
        new[] { result.MachineSourceFormat, result.ProcessSourceFormat, result.FilamentSourceFormat }
            .Should().OnlyContain(format => format == ProfileSourceFormat.BambuStudio);
        new[] { result.Machine.Outcome, result.Process.Outcome, result.Filament.Outcome }
            .Should().OnlyContain(outcome => outcome == ProfileResolutionOutcome.MappedEngine);
    }

    [Fact]
    public async Task BambuSelection_PinnedToOrca_FlattensOrcasOwnTwins()
    {
        var catalog = new TwoEngineCatalog();
        var (machine, process, filament) = catalog.StandardSet(Orca231, Bambu271);

        var result = await ResolveAsync(SelectionOver(catalog), machine[Bambu271], process[Bambu271], filament[Bambu271], Orca231, P1S);

        result.ProcessSettingsJson.Should().Contain(Marker("PROCESS", "0.20mm Standard @BBL X1C", Orca231)).And.NotContain("BambuStudio");
        result.FilamentSettingsJson.Should().Contain(Marker("FILAMENT", "Bambu PLA Basic @BBL X1C", Orca231)).And.NotContain("BambuStudio");
    }

    // "0.20mm Standard" is chosen once; if the pinned slicer has no such preset the print is blocked — it is never
    // sliced with the other engine's copy or with a similar preset of the pinned engine.
    [Fact]
    public async Task SelectedProcessMissingInThePinnedEngine_Blocks_NeverSlicesWithAnotherPreset()
    {
        var catalog = new TwoEngineCatalog();
        var machine = catalog.Machine(BambuVendor, MachineName, "GM014", Orca231, Bambu271);
        var filament = catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", [MachineName], Orca231, Bambu271);
        var standard = catalog.Process(BambuVendor, "0.20mm Standard @BBL X1C", "GP004", [MachineName], Orca231)[Orca231];
        catalog.Process(BambuVendor, "0.20mm Strength @BBL X1C", "GP010", [MachineName], Bambu271);

        var act = () => ResolveAsync(SelectionOver(catalog), machine[Orca231], standard, filament[Orca231], Bambu271, P1S);

        var failure = await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
        failure.Which.Code.Should().Be(nameof(ProfileResolutionOutcome.Missing));
        failure.Which.Message.Should().Contain("Process profile '0.20mm Standard @BBL X1C' is not in Bambu Studio 2.7.1").And.NotContain("Strength");
    }

    // ---------------------------------------------------------------- checks run on the RESOLVED rows

    [Fact]
    public async Task ResolvedFilamentThatDoesNotListTheResolvedMachine_IsBlocked()
    {
        var catalog = new TwoEngineCatalog();
        var machine = catalog.Machine(BambuVendor, MachineName, "GM014", Orca231, Bambu271);
        var process = catalog.Process(BambuVendor, "0.20mm Standard @BBL X1C", "GP004", [MachineName], Orca231, Bambu271);
        // Both engines' copies are the same preset, but the Bambu copy's compatibility list drifted away from the machine.
        var orcaFilament = catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", [MachineName], Orca231)[Orca231];
        catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", ["Bambu Lab X1 Carbon 0.4 nozzle"], Bambu271);

        var act = () => ResolveAsync(SelectionOver(catalog), machine[Orca231], process[Orca231], orcaFilament, Bambu271, P1S);

        var failure = await act.Should().ThrowAsync<ProfileSetBlockedException>();
        failure.Which.Code.Should().Be(nameof(ProfileResolutionOutcome.Incompatible));
        failure.Which.Message.Should().Contain("'Bambu PLA Basic @BBL X1C' is incompatible with P1S 0.4 mm");
    }

    [Fact]
    public async Task ResolvedMachineThatDoesNotFitThePrinter_IsBlocked()
    {
        var catalog = new TwoEngineCatalog();
        var (machine, process, filament) = catalog.StandardSet(Orca231, Bambu271);

        var act = () => ResolveAsync(SelectionOver(catalog), machine[Orca231], process[Orca231], filament[Orca231], Bambu271,
            new ProfileUseContext("Bambu", "P1S", 0.6m, "PLA"));

        (await act.Should().ThrowAsync<ProfileSetBlockedException>()).Which.Issues
            .Should().Contain(i => i.Kind == ProfileKind.Machine && i.Code == ProfileResolutionOutcome.Incompatible);
    }

    [Fact]
    public async Task ResolvedProcessThatDoesNotListTheResolvedMachine_IsBlocked()
    {
        var catalog = new TwoEngineCatalog();
        var machine = catalog.Machine(BambuVendor, MachineName, "GM014", Orca231, Bambu271);
        var filament = catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", [MachineName], Orca231, Bambu271);
        var orcaProcess = catalog.Process(BambuVendor, "0.20mm Standard @BBL X1C", "GP004", [MachineName], Orca231)[Orca231];
        catalog.Process(BambuVendor, "0.20mm Standard @BBL X1C", "GP004", ["Bambu Lab X1 Carbon 0.4 nozzle"], Bambu271);

        var act = () => ResolveAsync(SelectionOver(catalog), machine[Orca231], orcaProcess, filament[Orca231], Bambu271);

        (await act.Should().ThrowAsync<ProfileSetBlockedException>()).Which.Message.Should().Contain("process profile is incompatible with the machine profile");
    }

    [Fact]
    public async Task ResolvedSet_CarriesItsProvenance()
    {
        var catalog = new TwoEngineCatalog();
        var (machine, process, filament) = catalog.StandardSet(Orca231, Bambu271);

        var result = await ResolveAsync(SelectionOver(catalog), machine[Orca231], process[Orca231], filament[Orca231], Bambu271, P1S);

        result.Filament.Should().BeEquivalentTo(new ResolvedPreset(
            ProfileKind.Filament, filament[Orca231].Id, "Bambu PLA Basic @BBL X1C", filament[Bambu271].Id, "Bambu PLA Basic @BBL X1C",
            ProfileSourceFormat.BambuStudio, "2.7.1", "GFSA00", BambuVendor, ProfileResolutionOutcome.MappedEngine));
        result.FilamentProfileName.Should().Be("Bambu PLA Basic @BBL X1C");
    }
}
