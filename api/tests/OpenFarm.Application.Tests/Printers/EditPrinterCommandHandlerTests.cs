using OpenFarm.Application.Profiles;
using FluentAssertions;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Printers;

public class EditPrinterCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_NameOnlyChange_RenamesAndSkipsVerification()
    {
        var printer = CreatePrinter();
        var repository = new InMemoryPrinterRepository(printer);
        var connectorFactory = new RecordingPrinterConnectorFactory(verifies: true);
        var connectionManager = new RecordingPrinterConnectionManager();
        var handler = CreateHandler(repository, connectorFactory, connectionManager);

        var result = await handler.HandleAsync(Command(printer.Id, "Renamed"), CancellationToken.None);

        result.Name.Should().Be("Renamed");
        result.IpAddress.Should().Be(printer.IpAddress);
        connectorFactory.VerifyCallCount.Should().Be(0);
        connectionManager.StartCallCount.Should().Be(0);
        connectionManager.StopCallCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ConnectionFieldsSupplied_VerifiesBeforeSavingAndReconnects()
    {
        var printer = CreatePrinter();
        var repository = new InMemoryPrinterRepository(printer);
        var connectorFactory = new RecordingPrinterConnectorFactory(verifies: true);
        var connectionManager = new RecordingPrinterConnectionManager();
        var handler = CreateHandler(repository, connectorFactory, connectionManager);

        var result = await handler.HandleAsync(
            Command(printer.Id, "Renamed", ipAddress: "10.0.0.99", accessCode: "12345678"),
            CancellationToken.None);

        result.Name.Should().Be("Renamed");
        result.IpAddress.Should().Be("10.0.0.99");
        connectorFactory.VerifyCallCount.Should().Be(1);
        connectionManager.StopCallCount.Should().Be(1);
        connectionManager.StartCallCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_VerificationFails_ThrowsAndLeavesPrinterUnchanged()
    {
        var printer = CreatePrinter();
        var repository = new InMemoryPrinterRepository(printer);
        var connectorFactory = new RecordingPrinterConnectorFactory(verifies: false);
        var connectionManager = new RecordingPrinterConnectionManager();
        var handler = CreateHandler(repository, connectorFactory, connectionManager);

        var act = () => handler.HandleAsync(
            Command(printer.Id, "Renamed", ipAddress: "10.0.0.99", accessCode: "wrong"),
            CancellationToken.None);

        await act.Should().ThrowAsync<PrinterConnectionVerificationFailedException>();
        printer.Name.Should().Be("Printer 1");
        printer.IpAddress.Should().Be("10.0.0.5");
        connectionManager.StartCallCount.Should().Be(0);
        connectionManager.StopCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("WrongBrand", null, null, "brand")]
    [InlineData(null, "WrongModel", null, "modelCode")]
    [InlineData(null, null, "WrongSerial", "serialNumber")]
    public async Task HandleAsync_MismatchedIdentityField_ThrowsBeforeAnyMutation(
        string? brand, string? modelCode, string? serialNumber, string expectedField)
    {
        var printer = CreatePrinter();
        var repository = new InMemoryPrinterRepository(printer);
        var connectorFactory = new RecordingPrinterConnectorFactory(verifies: true);
        var connectionManager = new RecordingPrinterConnectionManager();
        var handler = CreateHandler(repository, connectorFactory, connectionManager);

        var act = () => handler.HandleAsync(
            Command(printer.Id, "Renamed", brand: brand, modelCode: modelCode, serialNumber: serialNumber),
            CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ImmutablePrinterFieldMismatchException>();
        thrown.Which.MismatchedFields.Should().ContainSingle(expectedField);
        printer.Name.Should().Be("Printer 1");
        connectorFactory.VerifyCallCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_MatchingIdentityFields_SucceedsAndRenames()
    {
        var printer = CreatePrinter();
        var repository = new InMemoryPrinterRepository(printer);
        var connectorFactory = new RecordingPrinterConnectorFactory(verifies: true);
        var connectionManager = new RecordingPrinterConnectionManager();
        var handler = CreateHandler(repository, connectorFactory, connectionManager);

        var result = await handler.HandleAsync(
            Command(printer.Id, "Renamed", brand: printer.Brand, modelCode: printer.ModelCode, serialNumber: printer.SerialNumber),
            CancellationToken.None);

        result.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task HandleAsync_UnknownPrinterId_ThrowsPrinterNotFound()
    {
        var repository = new InMemoryPrinterRepository();
        var connectorFactory = new RecordingPrinterConnectorFactory(verifies: true);
        var connectionManager = new RecordingPrinterConnectionManager();
        var handler = CreateHandler(repository, connectorFactory, connectionManager);

        var act = () => handler.HandleAsync(Command(Guid.NewGuid(), "Renamed"), CancellationToken.None);

        await act.Should().ThrowAsync<PrinterNotFoundException>();
    }

    // --- 009 US2: machine/process profile selection + compatibility gate (TDD, plan.md's Constitution Check) ---

    [Fact]
    public async Task HandleAsync_ProcessProfileWithNoMachineProfile_ThrowsRequiresMachineProfile()
    {
        var printer = CreatePrinter();
        var repository = new InMemoryPrinterRepository(printer);
        var processRepo = new FakeProcessProfileRepository();
        var process = ProcessProfile.CreateSystemProfile(
            "0.20mm Standard", ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GP004", true, null, sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);
        var handler = CreateHandler(repository, processRepo: processRepo);

        var act = () => handler.HandleAsync(
            Command(printer.Id, "Printer 1", processProfileId: processRepo.Rows[0].Id),
            CancellationToken.None);

        await act.Should().ThrowAsync<ProcessProfileRequiresMachineProfileException>();
    }

    [Fact]
    public async Task HandleAsync_ProcessProfileNotCompatibleWithMachine_ThrowsIncompatible()
    {
        var printer = CreatePrinter();
        var repository = new InMemoryPrinterRepository(printer);
        var machineRepo = new FakeMachineProfileRepository();
        var machine = MachineProfile.CreateSystemProfile(
            "Bambu Lab P1S 0.4 nozzle", ProfileSourceFormat.OrcaSlicer, null, """{"printer_model":"Bambu Lab P1S","nozzle_diameter":["0.4"]}""",
            "Bambulab", "GM010", true, "Bambu Lab P1S", null, sourceVersion: "1.0.0");
        await machineRepo.AddAsync(machine, CancellationToken.None);

        var processRepo = new FakeProcessProfileRepository();
        var process = ProcessProfile.CreateSystemProfile(
            "0.20mm Standard @BBL X1C", ProfileSourceFormat.OrcaSlicer, null, "{}",
            "Bambulab", "GP004", true, ["Bambu Lab X1 Carbon 0.4 nozzle"], sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);

        var handler = CreateHandler(repository, machineRepo: machineRepo, processRepo: processRepo);

        var act = () => handler.HandleAsync(
            Command(printer.Id, "Printer 1", machineProfileId: machine.Id, processProfileId: process.Id),
            CancellationToken.None);

        await act.Should().ThrowAsync<ProcessProfileIncompatibleWithMachineException>();
    }

    [Fact]
    public async Task HandleAsync_UnknownMachineProfileId_ThrowsInvalidProfileReference()
    {
        var printer = CreatePrinter();
        var repository = new InMemoryPrinterRepository(printer);
        var handler = CreateHandler(repository);

        var act = () => handler.HandleAsync(
            Command(printer.Id, "Printer 1", machineProfileId: Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidProfileReferenceException>();
    }

    [Fact]
    public async Task HandleAsync_CompatibleMachineAndProcess_LinksBothAndLogsGranularEvents()
    {
        var printer = CreatePrinter();
        var repository = new InMemoryPrinterRepository(printer);
        var machineRepo = new FakeMachineProfileRepository();
        var machine = MachineProfile.CreateSystemProfile(
            "Bambu Lab P1S 0.4 nozzle", ProfileSourceFormat.OrcaSlicer, null, """{"printer_model":"Bambu Lab P1S","nozzle_diameter":["0.4"]}""",
            "Bambulab", "GM001", true, "Bambu Lab P1S", null, sourceVersion: "1.0.0");
        await machineRepo.AddAsync(machine, CancellationToken.None);

        var processRepo = new FakeProcessProfileRepository();
        var process = ProcessProfile.CreateSystemProfile(
            "0.20mm Standard @BBL X1C", ProfileSourceFormat.OrcaSlicer, null, "{}",
            "Bambulab", "GP004", true, ["Bambu Lab P1S 0.4 nozzle"], sourceVersion: "1.0.0");
        await processRepo.AddAsync(process, CancellationToken.None);

        var activityLogWriter = new RecordingActivityLogWriter();
        var handler = CreateHandler(repository, machineRepo: machineRepo, processRepo: processRepo, activityLogWriter: activityLogWriter);

        var result = await handler.HandleAsync(
            Command(printer.Id, "Printer 1", machineProfileId: machine.Id, processProfileId: process.Id),
            CancellationToken.None);

        result.MachineProfileId.Should().Be(machine.Id);
        result.ProcessProfileId.Should().Be(process.Id);
        activityLogWriter.Entries.Should().Contain(e => e.EventType == "PrinterMachineProfileLinked");
        activityLogWriter.Entries.Should().Contain(e => e.EventType == "PrinterProcessProfileLinked");
    }

    [Fact]
    public async Task HandleAsync_ResendingTheSameAlreadyLinkedProfiles_DoesNotLogAnyGranularEvent()
    {
        var printer = CreatePrinter();
        var machineRepo = new FakeMachineProfileRepository();
        var machine = MachineProfile.CreateSystemProfile(
            "Bambu Lab X1 Carbon 0.4 nozzle", ProfileSourceFormat.OrcaSlicer, null, "{}",
            "Bambulab", "GM001", true, "Bambu Lab X1 Carbon", null, sourceVersion: "1.0.0");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);

        var repository = new InMemoryPrinterRepository(printer);
        var activityLogWriter = new RecordingActivityLogWriter();
        var handler = CreateHandler(repository, machineRepo: machineRepo, activityLogWriter: activityLogWriter);

        await handler.HandleAsync(Command(printer.Id, "Printer 1", machineProfileId: machine.Id), CancellationToken.None);

        activityLogWriter.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ClearingMachineProfile_LogsUnlinkedEvent()
    {
        var printer = CreatePrinter();
        var machineRepo = new FakeMachineProfileRepository();
        var machine = MachineProfile.CreateSystemProfile(
            "Bambu Lab X1 Carbon 0.4 nozzle", ProfileSourceFormat.OrcaSlicer, null, "{}",
            "Bambulab", "GM001", true, "Bambu Lab X1 Carbon", null, sourceVersion: "1.0.0");
        await machineRepo.AddAsync(machine, CancellationToken.None);
        printer.LinkMachineProfile(machine.Id);

        var repository = new InMemoryPrinterRepository(printer);
        var activityLogWriter = new RecordingActivityLogWriter();
        var handler = CreateHandler(repository, machineRepo: machineRepo, activityLogWriter: activityLogWriter);

        var result = await handler.HandleAsync(Command(printer.Id, "Printer 1"), CancellationToken.None);

        result.MachineProfileId.Should().BeNull();
        activityLogWriter.Entries.Should().Contain(e => e.EventType == "PrinterMachineProfileUnlinked");
    }

    // --- 012 US1: build-plate selection + compatibility gate (TDD, plan.md's Constitution Check) ---

    [Fact]
    public async Task HandleAsync_CompatibleBuildPlate_PersistsAndLogsSelectedEvent()
    {
        var printer = CreatePrinter();
        var repository = new InMemoryPrinterRepository(printer);
        var activityLogWriter = new RecordingActivityLogWriter();
        var handler = CreateHandler(repository, activityLogWriter: activityLogWriter);

        var result = await handler.HandleAsync(
            Command(printer.Id, "Printer 1", buildPlateType: BuildPlateType.TexturedPei),
            CancellationToken.None);

        result.BuildPlateType.Should().Be(BuildPlateType.TexturedPei);
        activityLogWriter.Entries.Should().Contain(e => e.EventType == "PrinterBuildPlateSelected");
    }

    [Fact]
    public async Task HandleAsync_IncompatibleBuildPlate_ThrowsIncompatibleBuildPlateAndLeavesPrinterUnchanged()
    {
        // Printer model is P1S (CreatePrinter) — compatible set is {Cool, HighTemp, TexturedPei,
        // Supertack}; Engineering is not in that set.
        var printer = CreatePrinter();
        var repository = new InMemoryPrinterRepository(printer);
        var handler = CreateHandler(repository);

        var act = () => handler.HandleAsync(
            Command(printer.Id, "Printer 1", buildPlateType: BuildPlateType.Engineering),
            CancellationToken.None);

        await act.Should().ThrowAsync<IncompatibleBuildPlateException>();
        printer.BuildPlateType.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_NoMachineProfilePrerequisite_BuildPlateSelectsOnItsOwn()
    {
        var printer = CreatePrinter();
        var repository = new InMemoryPrinterRepository(printer);
        var handler = CreateHandler(repository);

        var result = await handler.HandleAsync(
            Command(printer.Id, "Printer 1", buildPlateType: BuildPlateType.HighTemp),
            CancellationToken.None);

        result.MachineProfileId.Should().BeNull();
        result.BuildPlateType.Should().Be(BuildPlateType.HighTemp);
    }

    [Fact]
    public async Task HandleAsync_ClearingBuildPlate_LogsClearedEvent()
    {
        var printer = CreatePrinter();
        printer.SelectBuildPlate(BuildPlateType.HighTemp);

        var repository = new InMemoryPrinterRepository(printer);
        var activityLogWriter = new RecordingActivityLogWriter();
        var handler = CreateHandler(repository, activityLogWriter: activityLogWriter);

        var result = await handler.HandleAsync(Command(printer.Id, "Printer 1"), CancellationToken.None);

        result.BuildPlateType.Should().BeNull();
        activityLogWriter.Entries.Should().Contain(e => e.EventType == "PrinterBuildPlateCleared");
    }

    [Fact]
    public async Task HandleAsync_ResendingTheSameAlreadySelectedBuildPlate_DoesNotLogAnyGranularEvent()
    {
        var printer = CreatePrinter();
        printer.SelectBuildPlate(BuildPlateType.HighTemp);

        var repository = new InMemoryPrinterRepository(printer);
        var activityLogWriter = new RecordingActivityLogWriter();
        var handler = CreateHandler(repository, activityLogWriter: activityLogWriter);

        await handler.HandleAsync(
            Command(printer.Id, "Printer 1", buildPlateType: BuildPlateType.HighTemp),
            CancellationToken.None);

        activityLogWriter.Entries.Should().BeEmpty();
    }

    private static EditPrinterCommand Command(
        Guid printerId,
        string name,
        string? ipAddress = null,
        string? accessCode = null,
        string? brand = null,
        string? modelCode = null,
        string? serialNumber = null,
        Guid? machineProfileId = null,
        Guid? processProfileId = null,
        BuildPlateType? buildPlateType = null) =>
        new(printerId, name, ipAddress, accessCode, brand, modelCode, serialNumber, machineProfileId, processProfileId, buildPlateType, null, ActorType.System);

    private static EditPrinterCommandHandler CreateHandler(
        InMemoryPrinterRepository repository,
        RecordingPrinterConnectorFactory? connectorFactory = null,
        RecordingPrinterConnectionManager? connectionManager = null,
        FakeMachineProfileRepository? machineRepo = null,
        FakeProcessProfileRepository? processRepo = null,
        RecordingActivityLogWriter? activityLogWriter = null) =>
        new(
            connectorFactory ?? new RecordingPrinterConnectorFactory(verifies: true),
            repository,
            new InMemoryConnectionSecretRepository(),
            new PassthroughSecretProtector(),
            connectionManager ?? new RecordingPrinterConnectionManager(),
            machineRepo ?? new FakeMachineProfileRepository(),
            processRepo ?? new FakeProcessProfileRepository(),
            activityLogWriter ?? new RecordingActivityLogWriter(),
            new ProfileCompatibility(machineRepo ?? new FakeMachineProfileRepository(), new FakeFilamentProfileRepository()));

    private static Printer CreatePrinter()
    {
        var printer = Printer.Create("Printer 1", "Bambu", "P1S", "10.0.0.5", "SN-0001");
        printer.ObserveNozzle(0.4m, DateTimeOffset.UtcNow);
        return printer;
    }

    private sealed class InMemoryPrinterRepository(params Printer[] printers) : IPrinterRepository
    {
        private readonly Dictionary<Guid, Printer> _printers = printers.ToDictionary(p => p.Id);

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

    private sealed class InMemoryConnectionSecretRepository : IConnectionSecretRepository
    {
        public List<ConnectionSecret> Secrets { get; } = [];

        public Task AddAsync(ConnectionSecret connectionSecret, CancellationToken cancellationToken)
        {
            Secrets.Add(connectionSecret);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ConnectionSecret>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConnectionSecret>>(Secrets);

        public Task<ConnectionSecret?> GetByPrinterSerialNumberAsync(string printerSerialNumber, CancellationToken cancellationToken) =>
            Task.FromResult(Secrets.FirstOrDefault(s => s.PrinterSerialNumber == printerSerialNumber));
    }

    private sealed class PassthroughSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class RecordingPrinterConnectionManager : IPrinterConnectionManager
    {
        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public void StartConnection(Guid printerId, CancellationToken cancellationToken) => StartCallCount++;

        public void StopConnection(Guid printerId) => StopCallCount++;
    }

    private sealed class RecordingPrinterConnectorFactory(bool verifies) : IPrinterConnectorFactory
    {
        public int VerifyCallCount { get; private set; }

        public IPrinterConnector CreateConnector(PrinterConnectionDetails details) =>
            new RecordingConnector(this, verifies);

        public IPrinterCameraRelay CreateCameraRelay(PrinterConnectionDetails details) =>
            throw new NotSupportedException();

        private sealed class RecordingConnector(RecordingPrinterConnectorFactory owner, bool verifies) : IPrinterConnector
        {
            public Task<bool> VerifyAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
            {
                owner.VerifyCallCount++;
                return Task.FromResult(verifies);
            }

            public IAsyncEnumerable<PrinterConnectorEvent> ConnectAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, FilamentFeedMapping? feedMapping, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
        }
    }

    private sealed class RecordingActivityLogWriter : IActivityLogWriter
    {
        public List<ActivityLogEntry> Entries { get; } = [];

        public Task WriteAsync(ActivityLogEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
