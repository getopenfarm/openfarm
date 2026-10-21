using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Parts;

// TDD (write-first) — this resolution algorithm is a pure, deterministic function over already-
// persisted rows, the same bar the constitution sets for ResolveSlicerSelection. Covers research.md
// §5's rewrite: printer-targeted resolution, the fleet-representative-printer pick (smallest Guid
// among fully-configured printers) for the no-target case, and representative-filament fallback via
// RepresentativeFilamentResolver — farm-level defaults no longer exist anywhere in this path.
public class ResolveSlicingProfileSelectionTests
{
    // The System rows below are version 1.0.0 OrcaSlicer rows, so this pin needs no mapping.
    private static readonly SlicerRef Pin = new(SlicerEngine.OrcaSlicer, "1.0.0");
    private static readonly SlicerRef BambuPin = new(SlicerEngine.BambuStudio, "1.0.0");

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
        var resolver = CreateSelection(new FakePrinterRepository(), machines, processes, filaments);
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
        FakePrinterRepository printerRepo,
        FakeMachineProfileRepository machineRepo,
        FakeProcessProfileRepository processRepo,
        FakeFilamentProfileRepository filamentRepo,
        FakeSpoolRepository? spoolRepo = null,
        FakeFilamentRepository? filamentTrackingRepo = null,
        FakeFeedUnitRepository? feedUnitRepo = null)
    {
        if (spoolRepo is null)
        {
            spoolRepo = new FakeSpoolRepository();
            filamentTrackingRepo = new FakeFilamentRepository();
            feedUnitRepo = new FakeFeedUnitRepository();
            // Explicit production inventory fixture; settings/plate tests need a valid assigned spool.
            var profileRows = filamentRepo.ListAsync(default).GetAwaiter().GetResult();
            var profile = profileRows.Count > 0 ? profileRows[0] : null;
            if (profile is not null)
                foreach (var printer in printerRepo.ListAsync(default).GetAwaiter().GetResult())
                {
                    var diameter = printer.ModelCode == "P1S" ? 0.2m : 0.4m;
                    printer.ObserveNozzle(diameter, DateTimeOffset.UtcNow);
                    var filament = Filament.Create(Guid.NewGuid(), "PLA", "PLA");
                    var variant = filament.AddVariant("Black", "#000000");
                    filament.ReplaceProfileAssignments([FilamentProfileAssignment.Create(filament.Id, printer.Brand, printer.ModelCode, diameter, profile.Id)]);
                    filamentTrackingRepo.AddAsync(filament, default).GetAwaiter().GetResult();
                    var unit = FeedUnit.CreateConnectorDerived(printer.Id, FeedUnitKindCatalog.ExternalSpool, 0);
                    var slot = unit.AddFeedSlot(0);
                    feedUnitRepo.AddAsync(unit, default).GetAwaiter().GetResult();
                    var spool = Spool.Create(variant.Id, null, 1000, 800);
                    spool.LoadOntoFeedSlot(slot.Id);
                    spoolRepo.AddAsync(spool, default).GetAwaiter().GetResult();
                }
        }
        return new(printerRepo, machineRepo, processRepo, filamentRepo,
            new RepresentativeFilamentResolver(
                filamentRepo,
                spoolRepo ?? new FakeSpoolRepository(),
                filamentTrackingRepo ?? new FakeFilamentRepository(),
                feedUnitRepo ?? new FakeFeedUnitRepository(), printerRepo, machineRepo, new ProfileCompatibility(machineRepo, filamentRepo)),
            new ProfileCompatibility(machineRepo, filamentRepo),
            new ProfileResolver(machineRepo, processRepo, filamentRepo));
    }

    // 012 US2 — every printer here defaults to a compatible, real-plate-typed selection (X1C's
    // catalog set includes HighTemp) so the pre-existing success-path tests below stay unaffected by
    // the new build-plate requirement (FR-008); tests exercising the build-plate gate itself
    // (T023/T024) override or clear it explicitly.
    private static Printer CreatePrinter(string serial = "SN-1", BuildPlateType? buildPlateType = BuildPlateType.HighTemp)
    {
        var printer = Printer.Create("Printer A", "Bambu", "X1C", "10.0.0.5", serial);
        printer.ObserveNozzle(0.4m, DateTimeOffset.UtcNow);
        if (buildPlateType is { } value)
        {
            printer.SelectBuildPlate(value);
        }

        return printer;
    }

    private static FilamentProfile CreateSystemFilament(string vendorName, string externalId, string materialType) =>
        FilamentProfile.CreateSystemProfile(
            $"{vendorName} {materialType} {externalId}", ProfileSourceFormat.OrcaSlicer, null,
            $$"""{"type":"filament","filament_type":["{{materialType}}"],"hot_plate_temp":["55"]}""",
            vendorName, externalId, isSelectable: true, compatibleMachineNames: null, materialType: materialType, includedTemplateNames: null, sourceVersion: "1.0.0");

    [Fact]
    public async Task ResolveAsync_PrinterTargetedAndFullyConfigured_ReturnsAllThreeMergedJson()
    {
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();

        var printer = CreatePrinter();
        var machine = MachineProfile.Create("My Printer", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","printer_model":"Bambu Lab X1 Carbon","nozzle_diameter":["0.4"]}""");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);

        var process = ProcessProfile.CreateSystemProfile("0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.2"}""", "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: ["My Printer"], sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);

        var filament = CreateSystemFilament("Bambulab", "GFSA00", "PLA");
        await filamentRepo.AddAsync(filament, CancellationToken.None);

        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var result = await selection.ResolveAsync(printer.Id, "PLA", narrowedFilamentProfileId: null, Pin, CancellationToken.None);

        result.MachineSettingsJson.Should().Contain("\"nozzle_diameter\"");
        result.ProcessSettingsJson.Should().Contain("\"layer_height\"");
        result.FilamentSettingsJson.Should().Contain("\"filament_type\"");
    }

    [Fact]
    public async Task ResolveAsync_UnknownPrinterId_ThrowsSlicingProfileUnconfigured()
    {
        var selection = CreateSelection(new FakePrinterRepository(), new FakeMachineProfileRepository(), new FakeProcessProfileRepository(), new FakeFilamentProfileRepository());

        var act = async () => await selection.ResolveAsync(Guid.NewGuid(), "PLA", null, Pin, CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveAsync_PrinterHasNoMachineProfile_ThrowsSlicingProfileUnconfigured()
    {
        var printerRepo = new FakePrinterRepository();
        var printer = CreatePrinter();
        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, new FakeMachineProfileRepository(), new FakeProcessProfileRepository(), new FakeFilamentProfileRepository());

        var act = async () => await selection.ResolveAsync(printer.Id, "PLA", null, Pin, CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveAsync_PrinterHasMachineButNoProcessProfile_ThrowsSlicingProfileUnconfigured()
    {
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();

        var printer = CreatePrinter();
        var machine = MachineProfile.Create("My Printer", ProfileSourceFormat.OrcaSlicer, null, "{}");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, new FakeProcessProfileRepository(), new FakeFilamentProfileRepository());

        var act = async () => await selection.ResolveAsync(printer.Id, "PLA", null, Pin, CancellationToken.None);

        // Acceptance Scenario 2/SC-003 — names the printer and the missing selection, no partial result.
        (await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>())
            .WithMessage("*Printer A*process profile*");
    }

    [Fact]
    public async Task ResolveAsync_TwoPrintersOfDifferentModelsEachWithOwnSelections_EachResolvesItsOwnPresetsNeverAShared()
    {
        // Acceptance Scenario 1 — two printers, two independent selections, no shared default anywhere.
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();

        var printerA = Printer.Create("Printer A", "Bambu", "X1C", "10.0.0.10", "SN-A");
        printerA.SelectBuildPlate(BuildPlateType.HighTemp);
        var machineA = MachineProfile.Create("Machine A", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","printer_model":"Bambu Lab X1 Carbon","nozzle_diameter":["0.4"]}""");
        await machineRepo.AddAsync(machineA, CancellationToken.None);
        printerA.LinkMachineProfile(machineA.Id);
        var processA = ProcessProfile.CreateSystemProfile("Process A", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.1"}""", "Bambulab", "GPA", true, ["Machine A"], sourceVersion: "1.0.0");
        await processRepo.AddAsync(processA, CancellationToken.None);
        printerA.LinkProcessProfile(processA.Id);
        await printerRepo.AddAsync(printerA, CancellationToken.None);

        var printerB = Printer.Create("Printer B", "Bambu", "P1S", "10.0.0.11", "SN-B");
        printerB.SelectBuildPlate(BuildPlateType.HighTemp);
        var machineB = MachineProfile.Create("Machine B", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","printer_model":"Bambu Lab P1S","nozzle_diameter":["0.2"]}""");
        await machineRepo.AddAsync(machineB, CancellationToken.None);
        printerB.LinkMachineProfile(machineB.Id);
        var processB = ProcessProfile.CreateSystemProfile("Process B", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.3"}""", "Bambulab", "GPB", true, ["Machine B"], sourceVersion: "1.0.0");
        await processRepo.AddAsync(processB, CancellationToken.None);
        printerB.LinkProcessProfile(processB.Id);
        await printerRepo.AddAsync(printerB, CancellationToken.None);

        var filament = CreateSystemFilament("Bambulab", "GFSA00", "PLA");
        await filamentRepo.AddAsync(filament, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var resultA = await selection.ResolveAsync(printerA.Id, "PLA", null, Pin, CancellationToken.None);
        var resultB = await selection.ResolveAsync(printerB.Id, "PLA", null, Pin, CancellationToken.None);

        resultA.MachineSettingsJson.Should().Contain("\"0.4\"");
        resultA.ProcessSettingsJson.Should().Contain("\"0.1\"");
        resultB.MachineSettingsJson.Should().Contain("\"0.2\"");
        resultB.ProcessSettingsJson.Should().Contain("\"0.3\"");
    }

    [Fact]
    public async Task ResolveAsync_NoRepresentativeSystemFilamentForMaterial_ThrowsSlicingProfileUnconfigured()
    {
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();

        var printer = CreatePrinter();
        var machine = MachineProfile.Create("My Printer", ProfileSourceFormat.OrcaSlicer, null, "{}");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);

        var process = ProcessProfile.CreateSystemProfile("0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GP004", true, null, sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, new FakeFilamentProfileRepository());

        var act = async () => await selection.ResolveAsync(printer.Id, "PETG", null, Pin, CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveAsync_MachineProfileHasUnresolvedAncestorChain_ThrowsSlicingProfileUnconfigured()
    {
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();

        var printer = CreatePrinter();
        // Declares a base name that was never resolved to an id — an unresolved chain link.
        var machine = MachineProfile.Create("My Printer", ProfileSourceFormat.OrcaSlicer, "Some Missing Base", "{}");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);

        var process = ProcessProfile.CreateSystemProfile("0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GP004", true, null, sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);

        var filament = CreateSystemFilament("Bambulab", "GFSA00", "PLA");
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var act = async () => await selection.ResolveAsync(printer.Id, "PLA", null, Pin, CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveAsync_MachineProfileHasIncludedTemplateNames_FoldsResolvedFragmentIntoMachineSettingsJson()
    {
        // research.md §0a/§5 — Bambu Studio's `include` gcode-template fold-in: the leaf machine
        // preset itself carries no machine_start_gcode, only its named template fragment does.
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();

        var printer = CreatePrinter();

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
        printer.LinkMachineProfile(machine.Id);

        var process = ProcessProfile.CreateSystemProfile(
            "0.20mm Standard", ProfileSourceFormat.BambuStudio, null,
            """{"type":"process","layer_height":"0.2"}""", "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: ["X1C 0.4 nozzle"], sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);

        var filament = FilamentProfile.CreateSystemProfile("PLA", ProfileSourceFormat.BambuStudio, null, """{"filament_type":["PLA"],"hot_plate_temp":["55"]}""", "Bambulab", "GFSA00", true, null, "PLA", null, sourceVersion: "1.0.0");
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var result = await selection.ResolveProfilesAsync(machine.Id, process.Id, filament.Id, BuildPlateType.HighTemp, BambuPin, null, CancellationToken.None);

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
        var printerRepo = new FakePrinterRepository();
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

        var printer = CreatePrinter();
        printer.LinkMachineProfile(machine.Id);
        var process = ProcessProfile.CreateSystemProfile(
            "0.20mm Standard", ProfileSourceFormat.BambuStudio, null, """{"type":"process","layer_height":"0.2"}""",
            "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: [system.Name, machine.Name], sourceVersion: machineVersion);
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);
        var filament = FilamentProfile.CreateSystemProfile("PLA", ProfileSourceFormat.BambuStudio, null,
            """{"filament_type":["PLA"],"hot_plate_temp":["55"]}""", "Bambulab", "GFSA00", true, null, "PLA", null, sourceVersion: machineVersion);
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        return (CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo), machine, process, filament);
    }

    [Fact]
    public async Task ResolveAsync_NoTargetPrinter_EmptyFleet_ThrowsNoPrintersConfigured()
    {
        var selection = CreateSelection(new FakePrinterRepository(), new FakeMachineProfileRepository(), new FakeProcessProfileRepository(), new FakeFilamentProfileRepository());

        var act = async () => await selection.ResolveAsync(null, "PLA", null, Pin, CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveProfilesAsync_ExplicitSelections_AreIndependentOfFleet()
    {
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();

        var machine = MachineProfile.Create("Shared Machine", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine"}""");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        var process = ProcessProfile.CreateSystemProfile("Shared Process", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process"}""", "Bambulab", "GP004", true, null, sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        var filament = CreateSystemFilament("Bambulab", "GFSA00", "PLA");
        await filamentRepo.AddAsync(filament, CancellationToken.None);

        var unconfiguredPrinter = Printer.Create("Unconfigured", "Bambu", "X1C", "10.0.0.1", "SN-U");

        var configuredA = Printer.Create("A", "Bambu", "X1C", "10.0.0.2", "SN-A");
        configuredA.LinkMachineProfile(machine.Id);
        configuredA.LinkProcessProfile(process.Id);
        configuredA.SelectBuildPlate(BuildPlateType.HighTemp);

        var configuredB = Printer.Create("B", "Bambu", "X1C", "10.0.0.3", "SN-B");
        configuredB.LinkMachineProfile(machine.Id);
        configuredB.LinkProcessProfile(process.Id);
        configuredB.SelectBuildPlate(BuildPlateType.HighTemp);

        await printerRepo.AddAsync(unconfiguredPrinter, CancellationToken.None);
        await printerRepo.AddAsync(configuredA, CancellationToken.None);
        await printerRepo.AddAsync(configuredB, CancellationToken.None);

        var expectedRepresentativeId = new[] { configuredA.Id, configuredB.Id }.Order().First();

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var firstResult = await selection.ResolveProfilesAsync(machine.Id, process.Id, filament.Id, BuildPlateType.HighTemp, Pin, null, CancellationToken.None);
        var secondResult = await selection.ResolveProfilesAsync(machine.Id, process.Id, filament.Id, BuildPlateType.HighTemp, Pin, null, CancellationToken.None);

        firstResult.Should().BeEquivalentTo(secondResult);
        expectedRepresentativeId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_PrinterTargeted_PrefersItsOwnLoadedSpoolOverSystemCatalog()
    {
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();
        var spoolRepo = new FakeSpoolRepository();
        var filamentTrackingRepo = new FakeFilamentRepository();
        var feedUnitRepo = new FakeFeedUnitRepository();

        var printer = CreatePrinter();
        var machine = MachineProfile.Create("My Printer", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","printer_model":"Bambu Lab X1 Carbon","nozzle_diameter":["0.4"]}""");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);

        var process = ProcessProfile.CreateSystemProfile("0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.2"}""", "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: ["My Printer"], sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        // Catalog profile — must lose to the printer's own loaded spool.
        var catalogFilament = FilamentProfile.CreateSystemProfile(
            "Catalog PLA", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PLA"],"marker":"catalog-pick"}""",
            "Bambulab", "GFSA00", isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: "1.0.0");
        await filamentRepo.AddAsync(catalogFilament, CancellationToken.None);

        // The printer's own loaded spool's linked profile — must win.
        var loadedFilamentProfile = FilamentProfile.CreateSystemProfile(
            "Loaded PLA", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PLA"],"marker":"loaded-pick","hot_plate_temp":["55"]}""",
            "Polymaker", "P001", isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: "1.0.0");
        await filamentRepo.AddAsync(loadedFilamentProfile, CancellationToken.None);

        var trackedFilament = Filament.Create(Guid.NewGuid(), "PLA", "PLA");
        trackedFilament.ReplaceProfileAssignments([FilamentProfileAssignment.Create(trackedFilament.Id, "Bambu", "X1C", 0.4m, loadedFilamentProfile.Id)]);
        var variant = trackedFilament.AddVariant("Black", "#000000");
        await filamentTrackingRepo.AddAsync(trackedFilament, CancellationToken.None);

        var spool = Spool.Create(variant.Id, label: null, spoolWeightGrams: 1000, remainingWeightGrams: 800);
        await spoolRepo.AddAsync(spool, CancellationToken.None);

        var feedUnit = FeedUnit.CreateConnectorDerived(printer.Id, FeedUnitKindCatalog.ExternalSpool, unitIndex: 0);
        var feedSlot = feedUnit.AddFeedSlot(slotPosition: 0);
        await feedUnitRepo.AddAsync(feedUnit, CancellationToken.None);
        spool.LoadOntoFeedSlot(feedSlot.Id);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo, spoolRepo, filamentTrackingRepo, feedUnitRepo);

        var result = await selection.ResolveAsync(printer.Id, "PLA", narrowedFilamentProfileId: null, Pin, CancellationToken.None);

        result.FilamentSettingsJson.Should().Contain("loaded-pick");
        result.FilamentSettingsJson.Should().NotContain("catalog-pick");
    }

    [Fact]
    public async Task ResolveAsync_NarrowedFilamentProfileCannotBypassLoadedAssignment()
    {
        // A part constraint must agree with the physical spool assignment.
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();
        var spoolRepo = new FakeSpoolRepository();
        var filamentTrackingRepo = new FakeFilamentRepository();
        var feedUnitRepo = new FakeFeedUnitRepository();

        var printer = CreatePrinter();
        var machine = MachineProfile.Create("My Printer", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","printer_model":"Bambu Lab X1 Carbon","nozzle_diameter":["0.4"]}""");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);

        var process = ProcessProfile.CreateSystemProfile("0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.2"}""", "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: ["My Printer"], sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        // A fleet-loaded spool that would win tier 1 if matching ran at all.
        var wouldWinProfile = FilamentProfile.CreateSystemProfile(
            "Would-Win PLA", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PLA"],"marker":"would-win"}""",
            "Bambulab", "GFSA00", isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: "1.0.0");
        await filamentRepo.AddAsync(wouldWinProfile, CancellationToken.None);

        var trackedFilament = Filament.Create(Guid.NewGuid(), "PLA", "PLA");
        trackedFilament.ReplaceProfileAssignments([FilamentProfileAssignment.Create(trackedFilament.Id, "Bambu", "X1C", 0.4m, wouldWinProfile.Id)]);
        var variant = trackedFilament.AddVariant("Black", "#000000");
        await filamentTrackingRepo.AddAsync(trackedFilament, CancellationToken.None);

        var spool = Spool.Create(variant.Id, label: null, spoolWeightGrams: 1000, remainingWeightGrams: 800);
        await spoolRepo.AddAsync(spool, CancellationToken.None);

        var feedUnit = FeedUnit.CreateConnectorDerived(printer.Id, FeedUnitKindCatalog.ExternalSpool, unitIndex: 0);
        var feedSlot = feedUnit.AddFeedSlot(slotPosition: 0);
        await feedUnitRepo.AddAsync(feedUnit, CancellationToken.None);
        spool.LoadOntoFeedSlot(feedSlot.Id);

        // The manually narrowed, distinguishable target — must be the one that's actually used.
        var narrowedProfile = FilamentProfile.CreateSystemProfile(
            "Narrowed PETG", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PETG"],"marker":"narrowed-pick","hot_plate_temp":["55"]}""",
            "Prusament", "PT001", isSelectable: true, compatibleMachineNames: null, materialType: "PETG", includedTemplateNames: null, sourceVersion: "1.0.0");
        await filamentRepo.AddAsync(narrowedProfile, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo, spoolRepo, filamentTrackingRepo, feedUnitRepo);

        var act = () => selection.ResolveAsync(printer.Id, "PLA", narrowedFilamentProfileId: narrowedProfile.Id, Pin, CancellationToken.None);
        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*assignment*required filament profile*");
    }

    // --- 012 US2: build-plate-required resolution + bed-temp injection (TDD, research.md R8) ---

    [Fact]
    public async Task ResolveAsync_PrinterHasNoBuildPlateSelected_ThrowsSlicingProfileUnconfiguredNamingPrinter()
    {
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();

        var printer = CreatePrinter(buildPlateType: null);
        var machine = MachineProfile.Create("My Printer", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","printer_model":"Bambu Lab X1 Carbon","nozzle_diameter":["0.4"]}""");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);
        var process = ProcessProfile.CreateSystemProfile("0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.2"}""", "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: ["My Printer"], sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);
        var filament = CreateSystemFilament("Bambulab", "GFSA00", "PLA");
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var act = async () => await selection.ResolveAsync(printer.Id, "PLA", null, Pin, CancellationToken.None);

        (await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>())
            .WithMessage("*Printer A*build plate*");
    }

    [Fact]
    public async Task ResolveAsync_FilamentHasNoTempValueForSelectedPlate_ThrowsNamingFilamentAndPlateType()
    {
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();

        var printer = CreatePrinter(buildPlateType: BuildPlateType.Engineering);
        var machine = MachineProfile.Create("My Printer", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","printer_model":"Bambu Lab X1 Carbon","nozzle_diameter":["0.4"]}""");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);
        var process = ProcessProfile.CreateSystemProfile("0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.2"}""", "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: ["My Printer"], sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);

        // No eng_plate_temp key at all — OrcaSlicer's own "not applicable"/missing convention.
        var filament = FilamentProfile.CreateSystemProfile(
            "Bambulab PLA GFSA00", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PLA"],"hot_plate_temp":["55"]}""",
            "Bambulab", "GFSA00", isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: "1.0.0");
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var act = async () => await selection.ResolveAsync(printer.Id, "PLA", null, Pin, CancellationToken.None);

        (await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>())
            .WithMessage("*Bambulab PLA GFSA00*Engineering Plate*");
    }

    [Fact]
    public async Task ResolveAsync_FilamentHasZeroTempValueForSelectedPlate_ThrowsNamingFilamentAndPlateType()
    {
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();

        var printer = CreatePrinter(buildPlateType: BuildPlateType.Cool);
        var machine = MachineProfile.Create("My Printer", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","printer_model":"Bambu Lab X1 Carbon","nozzle_diameter":["0.4"]}""");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);
        var process = ProcessProfile.CreateSystemProfile("0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.2"}""", "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: ["My Printer"], sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);

        // cool_plate_temp explicitly "0" — the real OrcaSlicer "not applicable for this material/plate" value.
        var filament = FilamentProfile.CreateSystemProfile(
            "Bambulab PLA GFSA00", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PLA"],"cool_plate_temp":["0"]}""",
            "Bambulab", "GFSA00", isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: "1.0.0");
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var act = async () => await selection.ResolveAsync(printer.Id, "PLA", null, Pin, CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveAsync_PrinterFullyConfigured_InjectsCurrBedTypeMatchingSelectedPlate()
    {
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();

        var printer = CreatePrinter(buildPlateType: BuildPlateType.TexturedPei);
        var machine = MachineProfile.Create("My Printer", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","printer_model":"Bambu Lab X1 Carbon","nozzle_diameter":["0.4"]}""");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);
        var process = ProcessProfile.CreateSystemProfile("0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.2"}""", "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: ["My Printer"], sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);

        var filament = FilamentProfile.CreateSystemProfile(
            "Bambulab PLA GFSA00", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PLA"],"textured_plate_temp":["55"]}""",
            "Bambulab", "GFSA00", isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: "1.0.0");
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var result = await selection.ResolveAsync(printer.Id, "PLA", null, Pin, CancellationToken.None);

        result.ProcessSettingsJson.Should().Contain("\"curr_bed_type\":\"Textured PEI Plate\"");
    }

    [Fact]
    public async Task ResolveAsync_SC003_TwoBuildPlateSelectionsForSameFilamentMachineProcess_ProduceDifferentCurrBedType()
    {
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();

        var machine = MachineProfile.Create("Shared Machine", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","printer_model":"Bambu Lab X1 Carbon","nozzle_diameter":["0.4"]}""");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        var process = ProcessProfile.CreateSystemProfile("Shared Process", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.2"}""", "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: ["Shared Machine"], sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        var filament = FilamentProfile.CreateSystemProfile(
            "Bambulab PLA GFSA00", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PLA"],"hot_plate_temp":["55"],"textured_plate_temp":["55"]}""",
            "Bambulab", "GFSA00", isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: "1.0.0");
        await filamentRepo.AddAsync(filament, CancellationToken.None);

        var printerHighTemp = Printer.Create("Printer High Temp", "Bambu", "X1C", "10.0.0.20", "SN-HT");
        printerHighTemp.LinkMachineProfile(machine.Id);
        printerHighTemp.LinkProcessProfile(process.Id);
        printerHighTemp.SelectBuildPlate(BuildPlateType.HighTemp);
        await printerRepo.AddAsync(printerHighTemp, CancellationToken.None);

        var printerTexturedPei = Printer.Create("Printer Textured PEI", "Bambu", "X1C", "10.0.0.21", "SN-TP");
        printerTexturedPei.LinkMachineProfile(machine.Id);
        printerTexturedPei.LinkProcessProfile(process.Id);
        printerTexturedPei.SelectBuildPlate(BuildPlateType.TexturedPei);
        await printerRepo.AddAsync(printerTexturedPei, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var resultHighTemp = await selection.ResolveAsync(printerHighTemp.Id, "PLA", null, Pin, CancellationToken.None);
        var resultTexturedPei = await selection.ResolveAsync(printerTexturedPei.Id, "PLA", null, Pin, CancellationToken.None);

        resultHighTemp.ProcessSettingsJson.Should().Contain("\"curr_bed_type\":\"High Temp Plate\"");
        resultTexturedPei.ProcessSettingsJson.Should().Contain("\"curr_bed_type\":\"Textured PEI Plate\"");
        resultHighTemp.ProcessSettingsJson.Should().NotBe(resultTexturedPei.ProcessSettingsJson);
    }

    [Fact]
    public async Task ResolveAsync_AssignedPrinterWithoutBuildPlate_DoesNotUseAnotherPrinter()
    {
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();


        // Machine+process set but no build plate — must be excluded from the representative pick
        // regardless of Guid ordering (the filter itself, not the smallest-Guid tie-break, is what's
        // under test here).
        var machineNoPlate = MachineProfile.Create("Machine No Plate", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine"}""");
        await machineRepo.AddAsync(machineNoPlate, CancellationToken.None);
        var processNoPlate = ProcessProfile.CreateSystemProfile("Process No Plate", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process"}""", "Bambulab", "GP004", true, null, sourceVersion: "1.0.0");
        await processRepo.AddAsync(processNoPlate, CancellationToken.None);
        var noPlatePrinter = Printer.Create("No Plate", "Bambu", "X1C", "10.0.0.30", "SN-NOPLATE");
        noPlatePrinter.LinkMachineProfile(machineNoPlate.Id);
        noPlatePrinter.LinkProcessProfile(processNoPlate.Id);
        await printerRepo.AddAsync(noPlatePrinter, CancellationToken.None);

        var machineWithPlate = MachineProfile.Create("Machine With Plate", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine"}""");
        await machineRepo.AddAsync(machineWithPlate, CancellationToken.None);
        var processWithPlate = ProcessProfile.CreateSystemProfile("Process With Plate", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process"}""", "Bambulab", "GP005", true, null, sourceVersion: "1.0.0");
        await processRepo.AddAsync(processWithPlate, CancellationToken.None);
        var withPlatePrinter = Printer.Create("With Plate", "Bambu", "X1C", "10.0.0.31", "SN-PLATE");
        withPlatePrinter.LinkMachineProfile(machineWithPlate.Id);
        withPlatePrinter.LinkProcessProfile(processWithPlate.Id);
        withPlatePrinter.SelectBuildPlate(BuildPlateType.HighTemp);
        await printerRepo.AddAsync(withPlatePrinter, CancellationToken.None);

        var filament = CreateSystemFilament("Bambulab", "GFSA00", "PLA");
        await filamentRepo.AddAsync(filament, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var act = async () => await selection.ResolveAsync(noPlatePrinter.Id, "PLA", null, Pin, CancellationToken.None);
        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*No Plate*build plate*");
    }

    private sealed class FakePrinterRepository : IPrinterRepository
    {
        private readonly Dictionary<Guid, Printer> _printers = [];

        public Task AddAsync(Printer printer, CancellationToken cancellationToken)
        {
            _printers[printer.Id] = printer;
            return Task.CompletedTask;
        }

        public Task<Printer?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_printers.GetValueOrDefault(id));

        public Task<Printer?> GetBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken) =>
            Task.FromResult(_printers.Values.FirstOrDefault(p => p.SerialNumber == serialNumber));

        public Task<IReadOnlyList<Printer>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Printer>>(_printers.Values.ToList());

        public Task RemoveAsync(Printer printer, CancellationToken cancellationToken)
        {
            _printers.Remove(printer.Id);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
