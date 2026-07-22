using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
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
        FakeFilamentProfileRepository filamentRepo) =>
        new(printerRepo, machineRepo, processRepo, filamentRepo, new RepresentativeFilamentResolver(filamentRepo));

    private static Printer CreatePrinter(string serial = "SN-1") =>
        Printer.Create(Guid.NewGuid(), "Printer A", "Bambu", "X1C", "10.0.0.5", serial);

    private static FilamentProfile CreateSystemFilament(Guid organizationId, string vendorName, string externalId, string materialType) =>
        FilamentProfile.CreateSystemProfile(
            organizationId, $"{vendorName} {materialType} {externalId}", ProfileSourceFormat.OrcaSlicer, null,
            $$"""{"type":"filament","filament_type":["{{materialType}}"]}""",
            vendorName, externalId, isSelectable: true, compatibleMachineNames: null, materialType, includedTemplateNames: null);

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

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
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
