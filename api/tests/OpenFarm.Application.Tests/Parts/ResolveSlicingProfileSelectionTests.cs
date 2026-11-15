using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Parts;

// TDD (write-first) — this resolution is a pure, deterministic function over already-persisted rows, the same bar the
// constitution sets for ResolveSlicerSelection. A library estimate from named presets resolves machine, process and filament
// (ResolveProfilesAsync); production resolves only the filament (ResolveFilamentAsync) because a project keeps its own
// machine and process.
public class ResolveSlicingProfileSelectionTests
{
    // The System rows below are version 1.0.0 OrcaSlicer rows, so this pin needs no mapping.
    private static readonly SlicerRef Pin = new(SlicerEngine.OrcaSlicer, "1.0.0");
    private static readonly SlicerRef BambuPin = new(SlicerEngine.BambuStudio, "1.0.0");
    private static readonly ProfileUseContext PlaOnX1C = new("Bambu", "X1C", 0.4m, "PLA");

    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("cycle")]
    [InlineData("ambiguous")]
    public async Task BambuPresetsResolveInheritedTemplatesFromThePinnedBambuCatalog(string templateState)
    {
        var bambu = new SlicerRef(SlicerEngine.BambuStudio, "2.7.1");
        var machines = new FakeMachineProfileRepository();
        var processes = new FakeProcessProfileRepository();
        var filaments = new FakeFilamentProfileRepository();
        var machine = MachineProfile.Create("P1S", ProfileSourceFormat.BambuStudio, null, "{}");
        var process = ProcessProfile.CreateSystemProfile("Standard", ProfileSourceFormat.BambuStudio, null,
            "{}", "Bambu", "process", true, [machine.Name], sourceVersion: "2.7.1");
        var parent = FilamentProfile.Create("Bambu base", ProfileSourceFormat.BambuStudio, null,
            """{"filament_type":["PLA"],"hot_plate_temp":["55"],"include":["start template"]}""");
        var child = FilamentProfile.Create("Imported PLA", ProfileSourceFormat.BambuStudio, parent.Name,
            """{"filament_flow_ratio":["0.99"]}""");
        child.ResolveBaseProfile(parent.Id);
        await machines.AddAsync(machine, default);
        await processes.AddAsync(process, default);
        await filaments.AddAsync(parent, default);
        await filaments.AddAsync(child, default);
        // Identical template names in another engine catalog must not satisfy this import.
        await filaments.AddAsync(FilamentProfile.Create("start template", ProfileSourceFormat.OrcaSlicer, null,
            """{"filament_start_gcode":["wrong source"]}"""), default);
        if (templateState != "missing")
            await filaments.AddAsync(FilamentProfile.Create("start template", ProfileSourceFormat.BambuStudio, null,
                templateState == "cycle" ? """{"include":["start template"]}""" :
                """{"filament_start_gcode":["G28"],"filament_flow_ratio":["0.90"]}"""), default);
        if (templateState == "ambiguous")
            await filaments.AddAsync(FilamentProfile.Create("start template", ProfileSourceFormat.BambuStudio, null, "{}"), default);
        var resolver = CreateSelection(machines, processes, filaments);
        var resolve = () => resolver.ResolveProfilesAsync(machine.Id, process.Id, child.Id, BuildPlateType.HighTemp, bambu, null, default);
        if (templateState == "valid")
        {
            var result = await resolve();
            result.FilamentSettingsJson.Should().Contain("G28").And.Contain("0.99").And.NotContain("wrong source");
            result.FilamentSourceFormat.Should().Be(ProfileSourceFormat.BambuStudio);
            result.Pin.Should().Be(bambu);
            // Presets imported for Bambu Studio are never run by OrcaSlicer: no substitute, no mixed-origin slice.
            var underOrca = await ((Func<Task>)(() => resolver.ResolveProfilesAsync(machine.Id, process.Id, child.Id, BuildPlateType.HighTemp, Pin, null, default)))
                .Should().ThrowAsync<ProfileSetBlockedException>();
            underOrca.Which.Issues.Should().Contain(i => i.Kind == ProfileKind.Machine && i.Code == ProfileResolutionOutcome.UnsupportedOrigin)
                .And.Contain(i => i.Kind == ProfileKind.Filament && i.Code == ProfileResolutionOutcome.UnsupportedOrigin);
        }
        else await resolve.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    private static ResolveSlicingProfileSelection CreateSelection(
        FakeMachineProfileRepository machineRepo, FakeProcessProfileRepository processRepo, FakeFilamentProfileRepository filamentRepo) =>
        new(machineRepo, processRepo, filamentRepo, new ProfileResolver(machineRepo, processRepo, filamentRepo));

    private static FilamentProfile CreateSystemFilament(string json, string vendorName = "Bambulab", string externalId = "GFSA00", string materialType = "PLA") =>
        FilamentProfile.CreateSystemProfile(
            $"{vendorName} {materialType} {externalId}", ProfileSourceFormat.OrcaSlicer, null, json,
            vendorName, externalId, isSelectable: true, compatibleMachineNames: null, materialType: materialType, includedTemplateNames: null, sourceVersion: "1.0.0");

    private static FilamentProfile PlaFilament(string json = """{"type":"filament","filament_type":["PLA"],"hot_plate_temp":["55"],"textured_plate_temp":["55"]}""") =>
        CreateSystemFilament(json);

    private static async Task<(ResolveSlicingProfileSelection Selection, MachineProfile Machine, ProcessProfile Process)> MachineAndProcessAsync(
        FakeFilamentProfileRepository filamentRepo, params FilamentProfile[] filaments)
    {
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var machine = MachineProfile.Create("My Printer", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","printer_model":"Bambu Lab X1 Carbon","nozzle_diameter":["0.4"]}""");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        var process = ProcessProfile.CreateSystemProfile("0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.2"}""",
            "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: ["My Printer"], sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        foreach (var filament in filaments) await filamentRepo.AddAsync(filament, CancellationToken.None);
        return (CreateSelection(machineRepo, processRepo, filamentRepo), machine, process);
    }

    [Fact]
    public async Task ResolveProfilesAsync_ReturnsAllThreeMergedJson()
    {
        var filamentRepo = new FakeFilamentProfileRepository();
        var filament = PlaFilament();
        var (selection, machine, process) = await MachineAndProcessAsync(filamentRepo, filament);

        var result = await selection.ResolveProfilesAsync(machine.Id, process.Id, filament.Id, BuildPlateType.HighTemp, Pin, null, CancellationToken.None);

        result.MachineSettingsJson.Should().Contain("\"nozzle_diameter\"");
        result.ProcessSettingsJson.Should().Contain("\"layer_height\"");
        result.FilamentSettingsJson.Should().Contain("\"filament_type\"");
        result.FollowsProject.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveProfilesAsync_MachineProfileHasUnresolvedAncestorChain_ThrowsSlicingProfileUnconfigured()
    {
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();
        // Declares a base name that was never resolved to an id — an unresolved chain link.
        var machine = MachineProfile.Create("My Printer", ProfileSourceFormat.OrcaSlicer, "Some Missing Base", "{}");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        var process = ProcessProfile.CreateSystemProfile("0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GP004", true, null, sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        var filament = PlaFilament();
        await filamentRepo.AddAsync(filament, CancellationToken.None);

        var act = async () => await CreateSelection(machineRepo, processRepo, filamentRepo)
            .ResolveProfilesAsync(machine.Id, process.Id, filament.Id, BuildPlateType.HighTemp, Pin, null, CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveProfilesAsync_MachineProfileHasIncludedTemplateNames_FoldsResolvedFragmentIntoMachineSettingsJson()
    {
        // research.md §0a/§5 — Bambu Studio's `include` gcode-template fold-in: the leaf machine
        // preset itself carries no machine_start_gcode, only its named template fragment does.
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();

        var fragment = MachineProfile.CreateSystemProfile(
            "X1C template machine_start_gcode", ProfileSourceFormat.BambuStudio, null,
            """{"machine_start_gcode":"G28\n"}""",
            "Bambulab", null, isSelectable: false, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: "1.0.0");
        await machineRepo.AddAsync(fragment, CancellationToken.None);
        var machine = MachineProfile.CreateSystemProfile(
            "X1C 0.4 nozzle", ProfileSourceFormat.BambuStudio, null,
            """{"type":"machine","printer_model":"Bambu Lab X1 Carbon","nozzle_diameter":["0.4"]}""",
            "Bambulab", "GM001", isSelectable: true, vendorMachineModelName: null,
            includedTemplateNames: ["X1C template machine_start_gcode"], sourceVersion: "1.0.0");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        var process = ProcessProfile.CreateSystemProfile(
            "0.20mm Standard", ProfileSourceFormat.BambuStudio, null,
            """{"type":"process","layer_height":"0.2"}""", "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: ["X1C 0.4 nozzle"], sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        var filament = FilamentProfile.CreateSystemProfile("PLA", ProfileSourceFormat.BambuStudio, null, """{"filament_type":["PLA"],"hot_plate_temp":["55"]}""", "Bambulab", "GFSA00", true, null, "PLA", null, sourceVersion: "1.0.0");
        await filamentRepo.AddAsync(filament, CancellationToken.None);

        var result = await CreateSelection(machineRepo, processRepo, filamentRepo)
            .ResolveProfilesAsync(machine.Id, process.Id, filament.Id, BuildPlateType.HighTemp, BambuPin, null, CancellationToken.None);

        result.MachineSettingsJson.Should().Contain("\"machine_start_gcode\"");
        result.MachineSettingsJson.Should().Contain("\"nozzle_diameter\"");
    }

    // Two ingested versions carry a same-named include template with different content: a layer must pull the
    // template from the pinned catalog's version, never from whichever version a name lookup finds.
    [Theory]
    [InlineData("2.3.1", "OLD-GCODE", "NEW-GCODE")]
    [InlineData("2.4.2", "NEW-GCODE", "OLD-GCODE")]
    public async Task ResolveProfilesAsync_SystemMachineInclude_ResolvesInTheMachinesOwnCatalogVersion(string machineVersion, string expected, string other)
    {
        var (selection, machine, process, filament) = await TwoVersionIncludeFixtureAsync(machineVersion, userPresetOnTop: false);

        var result = await selection.ResolveProfilesAsync(machine.Id, process.Id, filament.Id, BuildPlateType.HighTemp,
            new SlicerRef(SlicerEngine.BambuStudio, machineVersion), null, CancellationToken.None);

        result.MachineSettingsJson.Should().Contain(expected).And.NotContain(other);
    }

    [Fact]
    public async Task ResolveProfilesAsync_UserMachineInclude_ResolvesInTheVersionOfItsSystemAncestor()
    {
        var (selection, machine, process, filament) = await TwoVersionIncludeFixtureAsync("2.3.1", userPresetOnTop: true);

        var result = await selection.ResolveProfilesAsync(machine.Id, process.Id, filament.Id, BuildPlateType.HighTemp,
            new SlicerRef(SlicerEngine.BambuStudio, "2.3.1"), null, CancellationToken.None);

        result.MachineSettingsJson.Should().Contain("OLD-GCODE").And.NotContain("NEW-GCODE");
    }

    private static async Task<(ResolveSlicingProfileSelection Selection, MachineProfile Machine, ProcessProfile Process, FilamentProfile Filament)>
        TwoVersionIncludeFixtureAsync(string machineVersion, bool userPresetOnTop)
    {
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();

        foreach (var (version, gcode) in new[] { ("2.3.1", "OLD-GCODE"), ("2.4.2", "NEW-GCODE") })
        {
            await machineRepo.AddAsync(MachineProfile.CreateSystemProfile(
                "X1C template machine_start_gcode", ProfileSourceFormat.BambuStudio, null,
                $$"""{"machine_start_gcode":"{{gcode}}"}""",
                "Bambulab", null, isSelectable: false, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: version), CancellationToken.None);
        }

        // The system machine declares the include itself, or (user-on-top case) leaves it to the user preset.
        var systemJson = userPresetOnTop
            ? """{"type":"machine","nozzle_diameter":["0.4"]}"""
            : """{"type":"machine","nozzle_diameter":["0.4"],"include":["X1C template machine_start_gcode"]}""";
        var system = MachineProfile.CreateSystemProfile(
            "X1C 0.4 nozzle", ProfileSourceFormat.BambuStudio, null, systemJson,
            "Bambulab", "GM001", isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: machineVersion);
        await machineRepo.AddAsync(system, CancellationToken.None);

        var machine = system;
        if (userPresetOnTop)
        {
            machine = MachineProfile.Create("My X1C", ProfileSourceFormat.BambuStudio, system.Name,
                """{"include":["X1C template machine_start_gcode"]}""");
            machine.ResolveBaseProfile(system.Id);
            await machineRepo.AddAsync(machine, CancellationToken.None);
        }

        var process = ProcessProfile.CreateSystemProfile(
            "0.20mm Standard", ProfileSourceFormat.BambuStudio, null, """{"type":"process","layer_height":"0.2"}""",
            "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: [system.Name, machine.Name], sourceVersion: machineVersion);
        await processRepo.AddAsync(process, CancellationToken.None);
        var filament = FilamentProfile.CreateSystemProfile("PLA", ProfileSourceFormat.BambuStudio, null,
            """{"filament_type":["PLA"],"hot_plate_temp":["55"]}""", "Bambulab", "GFSA00", true, null, "PLA", null, sourceVersion: machineVersion);
        await filamentRepo.AddAsync(filament, CancellationToken.None);

        return (CreateSelection(machineRepo, processRepo, filamentRepo), machine, process, filament);
    }

    [Fact]
    public async Task ResolveProfilesAsync_ExplicitSelections_ResolveTheSameEveryTime()
    {
        var filamentRepo = new FakeFilamentProfileRepository();
        var filament = PlaFilament();
        var (selection, machine, process) = await MachineAndProcessAsync(filamentRepo, filament);

        var first = await selection.ResolveProfilesAsync(machine.Id, process.Id, filament.Id, BuildPlateType.HighTemp, Pin, null, CancellationToken.None);
        var second = await selection.ResolveProfilesAsync(machine.Id, process.Id, filament.Id, BuildPlateType.HighTemp, Pin, null, CancellationToken.None);

        first.Should().BeEquivalentTo(second);
    }

    [Fact]
    public async Task ResolveProfilesAsync_FilamentHasNoTempValueForSelectedPlate_ThrowsNamingFilamentAndPlateType()
    {
        // No eng_plate_temp key at all — OrcaSlicer's own "not applicable"/missing convention.
        var filamentRepo = new FakeFilamentProfileRepository();
        var filament = PlaFilament("""{"type":"filament","filament_type":["PLA"],"hot_plate_temp":["55"]}""");
        var (selection, machine, process) = await MachineAndProcessAsync(filamentRepo, filament);

        var act = async () => await selection.ResolveProfilesAsync(machine.Id, process.Id, filament.Id, BuildPlateType.Engineering, Pin, null, CancellationToken.None);

        (await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>()).WithMessage("*Bambulab PLA GFSA00*Engineering Plate*");
    }

    [Fact]
    public async Task ResolveProfilesAsync_FilamentHasZeroTempValueForSelectedPlate_ThrowsNamingFilamentAndPlateType()
    {
        // cool_plate_temp explicitly "0" — the real OrcaSlicer "not applicable for this material/plate" value.
        var filamentRepo = new FakeFilamentProfileRepository();
        var filament = PlaFilament("""{"type":"filament","filament_type":["PLA"],"cool_plate_temp":["0"]}""");
        var (selection, machine, process) = await MachineAndProcessAsync(filamentRepo, filament);

        var act = async () => await selection.ResolveProfilesAsync(machine.Id, process.Id, filament.Id, BuildPlateType.Cool, Pin, null, CancellationToken.None);

        (await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>()).WithMessage("*Bambulab PLA GFSA00*Cool Plate*");
    }

    [Theory]
    [InlineData(BuildPlateType.HighTemp, "High Temp Plate")]
    [InlineData(BuildPlateType.TexturedPei, "Textured PEI Plate")]
    public async Task ResolveProfilesAsync_InjectsCurrBedTypeMatchingThePlate(BuildPlateType plate, string expected)
    {
        var filamentRepo = new FakeFilamentProfileRepository();
        var filament = PlaFilament();
        var (selection, machine, process) = await MachineAndProcessAsync(filamentRepo, filament);

        var result = await selection.ResolveProfilesAsync(machine.Id, process.Id, filament.Id, plate, Pin, null, CancellationToken.None);

        result.ProcessSettingsJson.Should().Contain($"\"curr_bed_type\":\"{expected}\"");
    }

    // Production: the project keeps its own machine and process; only the filament is resolved.
    [Fact]
    public async Task ResolveFilamentAsync_ResolvesOnlyTheFilament_AndTheProjectKeepsItsMachineAndProcess()
    {
        var filamentRepo = new FakeFilamentProfileRepository();
        var filament = PlaFilament();
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        var selection = CreateSelection(new FakeMachineProfileRepository(), new FakeProcessProfileRepository(), filamentRepo);

        var result = await selection.ResolveFilamentAsync(filament.Id, BuildPlateType.TexturedPei, "Bambu Lab X1 Carbon 0.4 nozzle", Pin, PlaOnX1C, CancellationToken.None);

        result.FollowsProject.Should().BeTrue();
        result.MachineSettingsJson.Should().BeNull();
        result.ProcessSettingsJson.Should().BeNull();
        result.Machine.Should().BeNull();
        result.Process.Should().BeNull();
        result.FilamentSettingsJson.Should().Contain("\"filament_type\"");
        result.Pin.Should().Be(Pin);
    }

    [Fact]
    public async Task ResolveFilamentAsync_FilamentHasNoTempForThePlateTheFileIsMadeFor_IsBlocked()
    {
        var filamentRepo = new FakeFilamentProfileRepository();
        var filament = PlaFilament("""{"type":"filament","filament_type":["PLA"],"hot_plate_temp":["55"]}""");
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        var selection = CreateSelection(new FakeMachineProfileRepository(), new FakeProcessProfileRepository(), filamentRepo);

        var act = async () => await selection.ResolveFilamentAsync(filament.Id, BuildPlateType.Cool, null, Pin, PlaOnX1C, CancellationToken.None);

        (await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>()).WithMessage("*Bambulab PLA GFSA00*Cool Plate*");
    }

    [Fact]
    public async Task ResolveFilamentAsync_FilamentOfAnotherMaterial_IsBlocked()
    {
        var filamentRepo = new FakeFilamentProfileRepository();
        var filament = PlaFilament("""{"type":"filament","filament_type":["PETG"],"hot_plate_temp":["70"]}""");
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        var selection = CreateSelection(new FakeMachineProfileRepository(), new FakeProcessProfileRepository(), filamentRepo);

        var act = async () => await selection.ResolveFilamentAsync(filament.Id, BuildPlateType.HighTemp, null, Pin, PlaOnX1C, CancellationToken.None);

        (await act.Should().ThrowAsync<ProfileSetBlockedException>()).Which.Issues.Should().ContainSingle(i => i.Kind == ProfileKind.Filament && i.Message.Contains("material"));
    }

    [Fact]
    public async Task ResolveFilamentAsync_FilamentNotListedForTheFilesMachinePreset_IsBlocked()
    {
        var machineRepo = new FakeMachineProfileRepository();
        await machineRepo.AddAsync(MachineProfile.CreateSystemProfile("Bambu Lab X1 Carbon 0.4 nozzle", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"machine"}""", "Bambulab", "GM001", isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: "1.0.0"), default);
        var filamentRepo = new FakeFilamentProfileRepository();
        var filament = PlaFilament("""{"type":"filament","filament_type":["PLA"],"hot_plate_temp":["55"],"compatible_printers":["Bambu Lab P1S 0.4 nozzle"]}""");
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        var selection = CreateSelection(machineRepo, new FakeProcessProfileRepository(), filamentRepo);

        var act = async () => await selection.ResolveFilamentAsync(filament.Id, BuildPlateType.HighTemp, "Bambu Lab X1 Carbon 0.4 nozzle", Pin, PlaOnX1C, CancellationToken.None);

        (await act.Should().ThrowAsync<ProfileSetBlockedException>()).Which.Issues.Should().ContainSingle(i => i.Message.Contains("Bambu Lab X1 Carbon 0.4 nozzle"));
    }

    [Fact]
    public async Task ResolveFilamentAsync_FileMachinePresetNotInTheCatalog_IsNotChecked_TheSlicerRefusesARealMismatch()
    {
        // A custom machine preset has no catalog row: nothing to compare the filament's compatible printers with.
        var filamentRepo = new FakeFilamentProfileRepository();
        var filament = PlaFilament("""{"type":"filament","filament_type":["PLA"],"hot_plate_temp":["55"],"compatible_printers":["Bambu Lab P1S 0.4 nozzle"]}""");
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        var selection = CreateSelection(new FakeMachineProfileRepository(), new FakeProcessProfileRepository(), filamentRepo);

        var result = await selection.ResolveFilamentAsync(filament.Id, BuildPlateType.HighTemp, "My custom machine", Pin, PlaOnX1C, CancellationToken.None);

        result.FollowsProject.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveFilamentAsync_FilamentTheCatalogDoesNotHave_IsBlocked()
    {
        var selection = CreateSelection(new FakeMachineProfileRepository(), new FakeProcessProfileRepository(), new FakeFilamentProfileRepository());

        var act = async () => await selection.ResolveFilamentAsync(Guid.NewGuid(), BuildPlateType.HighTemp, null, Pin, PlaOnX1C, CancellationToken.None);

        (await act.Should().ThrowAsync<ProfileSetBlockedException>()).Which.Issues.Should().ContainSingle(i => i.Kind == ProfileKind.Filament);
    }
}
