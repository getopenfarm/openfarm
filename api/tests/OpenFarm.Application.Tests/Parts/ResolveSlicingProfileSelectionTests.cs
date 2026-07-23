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
// RepresentativeFilamentResolver — organization-level defaults no longer exist anywhere in this path.
public class ResolveSlicingProfileSelectionTests
{
    private static ResolveSlicingProfileSelection CreateSelection(
        FakePrinterRepository printerRepo,
        FakeMachineProfileRepository machineRepo,
        FakeProcessProfileRepository processRepo,
        FakeFilamentProfileRepository filamentRepo,
        FakeSpoolRepository? spoolRepo = null,
        FakeFilamentRepository? filamentTrackingRepo = null,
        FakeFeedUnitRepository? feedUnitRepo = null) =>
        new(printerRepo, machineRepo, processRepo, filamentRepo,
            new RepresentativeFilamentResolver(
                filamentRepo,
                spoolRepo ?? new FakeSpoolRepository(),
                filamentTrackingRepo ?? new FakeFilamentRepository(),
                feedUnitRepo ?? new FakeFeedUnitRepository()));

    private static Printer CreatePrinter(string serial = "SN-1") =>
        Printer.Create(Guid.NewGuid(), "Printer A", "Bambu", "X1C", "10.0.0.5", serial);

    private static FilamentProfile CreateSystemFilament(Guid organizationId, string vendorName, string externalId, string materialType) =>
        FilamentProfile.CreateSystemProfile(
            organizationId, $"{vendorName} {materialType} {externalId}", ProfileSourceFormat.OrcaSlicer, null,
            $$"""{"type":"filament","filament_type":["{{materialType}}"]}""",
            vendorName, externalId, isSelectable: true, compatibleMachineNames: null, materialType: materialType, includedTemplateNames: null);

    [Fact]
    public async Task ResolveAsync_PrinterTargetedAndFullyConfigured_ReturnsAllThreeMergedJson()
    {
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();

        var printer = CreatePrinter();
        var machine = MachineProfile.Create(printer.OrganizationId, "My Printer", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","nozzle_diameter":["0.4"]}""");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);

        var process = ProcessProfile.CreateSystemProfile(printer.OrganizationId, "0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.2"}""", "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: ["My Printer"]);
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);

        var filament = CreateSystemFilament(printer.OrganizationId, "Bambulab", "GFSA00", "PLA");
        await filamentRepo.AddAsync(filament, CancellationToken.None);

        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var result = await selection.ResolveAsync(printer.Id, "PLA", narrowedFilamentProfileId: null, CancellationToken.None);

        result.MachineSettingsJson.Should().Contain("\"nozzle_diameter\"");
        result.ProcessSettingsJson.Should().Contain("\"layer_height\"");
        result.FilamentSettingsJson.Should().Contain("\"filament_type\"");
    }

    [Fact]
    public async Task ResolveAsync_UnknownPrinterId_ThrowsSlicingProfileUnconfigured()
    {
        var selection = CreateSelection(new FakePrinterRepository(), new FakeMachineProfileRepository(), new FakeProcessProfileRepository(), new FakeFilamentProfileRepository());

        var act = async () => await selection.ResolveAsync(Guid.NewGuid(), "PLA", null, CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveAsync_PrinterHasNoMachineProfile_ThrowsSlicingProfileUnconfigured()
    {
        var printerRepo = new FakePrinterRepository();
        var printer = CreatePrinter();
        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, new FakeMachineProfileRepository(), new FakeProcessProfileRepository(), new FakeFilamentProfileRepository());

        var act = async () => await selection.ResolveAsync(printer.Id, "PLA", null, CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveAsync_PrinterHasMachineButNoProcessProfile_ThrowsSlicingProfileUnconfigured()
    {
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();

        var printer = CreatePrinter();
        var machine = MachineProfile.Create(printer.OrganizationId, "My Printer", ProfileSourceFormat.OrcaSlicer, null, "{}");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, new FakeProcessProfileRepository(), new FakeFilamentProfileRepository());

        var act = async () => await selection.ResolveAsync(printer.Id, "PLA", null, CancellationToken.None);

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

        var printerA = Printer.Create(Guid.NewGuid(), "Printer A", "Bambu", "X1C", "10.0.0.10", "SN-A");
        var machineA = MachineProfile.Create(printerA.OrganizationId, "Machine A", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","nozzle_diameter":["0.4"]}""");
        await machineRepo.AddAsync(machineA, CancellationToken.None);
        printerA.LinkMachineProfile(machineA.Id);
        var processA = ProcessProfile.CreateSystemProfile(printerA.OrganizationId, "Process A", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.1"}""", "Bambulab", "GPA", true, ["Machine A"]);
        await processRepo.AddAsync(processA, CancellationToken.None);
        printerA.LinkProcessProfile(processA.Id);
        await printerRepo.AddAsync(printerA, CancellationToken.None);

        var printerB = Printer.Create(Guid.NewGuid(), "Printer B", "Bambu", "P1S", "10.0.0.11", "SN-B");
        var machineB = MachineProfile.Create(printerB.OrganizationId, "Machine B", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","nozzle_diameter":["0.2"]}""");
        await machineRepo.AddAsync(machineB, CancellationToken.None);
        printerB.LinkMachineProfile(machineB.Id);
        var processB = ProcessProfile.CreateSystemProfile(printerB.OrganizationId, "Process B", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.3"}""", "Bambulab", "GPB", true, ["Machine B"]);
        await processRepo.AddAsync(processB, CancellationToken.None);
        printerB.LinkProcessProfile(processB.Id);
        await printerRepo.AddAsync(printerB, CancellationToken.None);

        var filament = CreateSystemFilament(printerA.OrganizationId, "Bambulab", "GFSA00", "PLA");
        await filamentRepo.AddAsync(filament, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var resultA = await selection.ResolveAsync(printerA.Id, "PLA", null, CancellationToken.None);
        var resultB = await selection.ResolveAsync(printerB.Id, "PLA", null, CancellationToken.None);

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
        var machine = MachineProfile.Create(printer.OrganizationId, "My Printer", ProfileSourceFormat.OrcaSlicer, null, "{}");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);

        var process = ProcessProfile.CreateSystemProfile(printer.OrganizationId, "0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GP004", true, null);
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, new FakeFilamentProfileRepository());

        var act = async () => await selection.ResolveAsync(printer.Id, "PETG", null, CancellationToken.None);

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
        var machine = MachineProfile.Create(printer.OrganizationId, "My Printer", ProfileSourceFormat.OrcaSlicer, "Some Missing Base", "{}");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);

        var process = ProcessProfile.CreateSystemProfile(printer.OrganizationId, "0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GP004", true, null);
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);

        var filament = CreateSystemFilament(printer.OrganizationId, "Bambulab", "GFSA00", "PLA");
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var act = async () => await selection.ResolveAsync(printer.Id, "PLA", null, CancellationToken.None);

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
            printer.OrganizationId, "X1C template machine_start_gcode", ProfileSourceFormat.BambuStudio, null,
            """{"machine_start_gcode":"G28\n"}""",
            "Bambulab", null, isSelectable: false, vendorMachineModelName: null, includedTemplateNames: null);
        await machineRepo.AddAsync(fragment, CancellationToken.None);

        var machine = MachineProfile.CreateSystemProfile(
            printer.OrganizationId, "X1C 0.4 nozzle", ProfileSourceFormat.BambuStudio, null,
            """{"type":"machine","nozzle_diameter":["0.4"]}""",
            "Bambulab", "GM001", isSelectable: true, vendorMachineModelName: null,
            includedTemplateNames: ["X1C template machine_start_gcode"]);
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);

        var process = ProcessProfile.CreateSystemProfile(
            printer.OrganizationId, "0.20mm Standard", ProfileSourceFormat.BambuStudio, null,
            """{"type":"process","layer_height":"0.2"}""", "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: ["X1C 0.4 nozzle"]);
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);

        var filament = CreateSystemFilament(printer.OrganizationId, "Bambulab", "GFSA00", "PLA");
        await filamentRepo.AddAsync(filament, CancellationToken.None);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var result = await selection.ResolveAsync(printer.Id, "PLA", narrowedFilamentProfileId: null, CancellationToken.None);

        result.MachineSettingsJson.Should().Contain("\"machine_start_gcode\"");
        result.MachineSettingsJson.Should().Contain("\"nozzle_diameter\"");
    }

    [Fact]
    public async Task ResolveAsync_NoTargetPrinter_EmptyFleet_ThrowsNoPrintersConfigured()
    {
        var selection = CreateSelection(new FakePrinterRepository(), new FakeMachineProfileRepository(), new FakeProcessProfileRepository(), new FakeFilamentProfileRepository());

        var act = async () => await selection.ResolveAsync(null, "PLA", null, CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveAsync_NoTargetPrinter_PicksSmallestGuidAmongFullyConfiguredPrinters()
    {
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();

        var organizationId = Guid.NewGuid();
        var machine = MachineProfile.Create(organizationId, "Shared Machine", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine"}""");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        var process = ProcessProfile.CreateSystemProfile(organizationId, "Shared Process", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process"}""", "Bambulab", "GP004", true, null);
        await processRepo.AddAsync(process, CancellationToken.None);
        var filament = CreateSystemFilament(organizationId, "Bambulab", "GFSA00", "PLA");
        await filamentRepo.AddAsync(filament, CancellationToken.None);

        var unconfiguredPrinter = Printer.Create(organizationId, "Unconfigured", "Bambu", "X1C", "10.0.0.1", "SN-U");

        var configuredA = Printer.Create(organizationId, "A", "Bambu", "X1C", "10.0.0.2", "SN-A");
        configuredA.LinkMachineProfile(machine.Id);
        configuredA.LinkProcessProfile(process.Id);

        var configuredB = Printer.Create(organizationId, "B", "Bambu", "X1C", "10.0.0.3", "SN-B");
        configuredB.LinkMachineProfile(machine.Id);
        configuredB.LinkProcessProfile(process.Id);

        await printerRepo.AddAsync(unconfiguredPrinter, CancellationToken.None);
        await printerRepo.AddAsync(configuredA, CancellationToken.None);
        await printerRepo.AddAsync(configuredB, CancellationToken.None);

        var expectedRepresentativeId = new[] { configuredA.Id, configuredB.Id }.Order().First();

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo);

        var firstResult = await selection.ResolveAsync(null, "PLA", null, CancellationToken.None);
        var secondResult = await selection.ResolveAsync(null, "PLA", null, CancellationToken.None);

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
        var machine = MachineProfile.Create(printer.OrganizationId, "My Printer", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","nozzle_diameter":["0.4"]}""");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);

        var process = ProcessProfile.CreateSystemProfile(printer.OrganizationId, "0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.2"}""", "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: ["My Printer"]);
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        // Catalog profile — must lose to the printer's own loaded spool.
        var catalogFilament = FilamentProfile.CreateSystemProfile(
            printer.OrganizationId, "Catalog PLA", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PLA"],"marker":"catalog-pick"}""",
            "Bambulab", "GFSA00", isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null);
        await filamentRepo.AddAsync(catalogFilament, CancellationToken.None);

        // The printer's own loaded spool's linked profile — must win.
        var loadedFilamentProfile = FilamentProfile.CreateSystemProfile(
            printer.OrganizationId, "Loaded PLA", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PLA"],"marker":"loaded-pick"}""",
            "Polymaker", "P001", isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null);
        await filamentRepo.AddAsync(loadedFilamentProfile, CancellationToken.None);

        var trackedFilament = Filament.Create(printer.OrganizationId, Guid.NewGuid(), "PLA", "PLA", loadedFilamentProfile.Id);
        var variant = trackedFilament.AddVariant("Black", "#000000");
        await filamentTrackingRepo.AddAsync(trackedFilament, CancellationToken.None);

        var spool = Spool.Create(printer.OrganizationId, variant.Id, label: null, spoolWeightGrams: 1000, remainingWeightGrams: 800);
        await spoolRepo.AddAsync(spool, CancellationToken.None);

        var feedUnit = FeedUnit.CreateConnectorDerived(printer.OrganizationId, printer.Id, FeedUnitKindCatalog.ExternalSpool, unitIndex: 0);
        var feedSlot = feedUnit.AddFeedSlot(slotPosition: 0);
        await feedUnitRepo.AddAsync(feedUnit, CancellationToken.None);
        spool.LoadOntoFeedSlot(feedSlot.Id);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo, spoolRepo, filamentTrackingRepo, feedUnitRepo);

        var result = await selection.ResolveAsync(printer.Id, "PLA", narrowedFilamentProfileId: null, CancellationToken.None);

        result.FilamentSettingsJson.Should().Contain("loaded-pick");
        result.FilamentSettingsJson.Should().NotContain("catalog-pick");
    }

    [Fact]
    public async Task ResolveAsync_NarrowedFilamentProfile_NeverInvokesMatchingCapabilityEvenWhenAFleetLoadedSpoolWouldWin()
    {
        // FR-011 regression — a manually narrowed filament requirement must bypass the matching
        // capability entirely, even when a fleet-loaded spool would otherwise win tier 1/2.
        var printerRepo = new FakePrinterRepository();
        var machineRepo = new FakeMachineProfileRepository();
        var processRepo = new FakeProcessProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();
        var spoolRepo = new FakeSpoolRepository();
        var filamentTrackingRepo = new FakeFilamentRepository();
        var feedUnitRepo = new FakeFeedUnitRepository();

        var printer = CreatePrinter();
        var machine = MachineProfile.Create(printer.OrganizationId, "My Printer", ProfileSourceFormat.OrcaSlicer, null, """{"type":"machine","nozzle_diameter":["0.4"]}""");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);

        var process = ProcessProfile.CreateSystemProfile(printer.OrganizationId, "0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, """{"type":"process","layer_height":"0.2"}""", "Bambulab", "GP004", isSelectable: true, compatibleMachineNames: ["My Printer"]);
        await processRepo.AddAsync(process, CancellationToken.None);
        printer.LinkProcessProfile(process.Id);
        await printerRepo.AddAsync(printer, CancellationToken.None);

        // A fleet-loaded spool that would win tier 1 if matching ran at all.
        var wouldWinProfile = FilamentProfile.CreateSystemProfile(
            printer.OrganizationId, "Would-Win PLA", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PLA"],"marker":"would-win"}""",
            "Bambulab", "GFSA00", isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null);
        await filamentRepo.AddAsync(wouldWinProfile, CancellationToken.None);

        var trackedFilament = Filament.Create(printer.OrganizationId, Guid.NewGuid(), "PLA", "PLA", wouldWinProfile.Id);
        var variant = trackedFilament.AddVariant("Black", "#000000");
        await filamentTrackingRepo.AddAsync(trackedFilament, CancellationToken.None);

        var spool = Spool.Create(printer.OrganizationId, variant.Id, label: null, spoolWeightGrams: 1000, remainingWeightGrams: 800);
        await spoolRepo.AddAsync(spool, CancellationToken.None);

        var feedUnit = FeedUnit.CreateConnectorDerived(printer.OrganizationId, printer.Id, FeedUnitKindCatalog.ExternalSpool, unitIndex: 0);
        var feedSlot = feedUnit.AddFeedSlot(slotPosition: 0);
        await feedUnitRepo.AddAsync(feedUnit, CancellationToken.None);
        spool.LoadOntoFeedSlot(feedSlot.Id);

        // The manually narrowed, distinguishable target — must be the one that's actually used.
        var narrowedProfile = FilamentProfile.CreateSystemProfile(
            printer.OrganizationId, "Narrowed PETG", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PETG"],"marker":"narrowed-pick"}""",
            "Prusament", "PT001", isSelectable: true, compatibleMachineNames: null, materialType: "PETG", includedTemplateNames: null);
        await filamentRepo.AddAsync(narrowedProfile, CancellationToken.None);

        var selection = CreateSelection(printerRepo, machineRepo, processRepo, filamentRepo, spoolRepo, filamentTrackingRepo, feedUnitRepo);

        var result = await selection.ResolveAsync(printer.Id, "PLA", narrowedFilamentProfileId: narrowedProfile.Id, CancellationToken.None);

        result.FilamentSettingsJson.Should().Contain("narrowed-pick");
        result.FilamentSettingsJson.Should().NotContain("would-win");
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
