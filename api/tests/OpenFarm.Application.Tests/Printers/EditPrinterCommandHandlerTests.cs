using FluentAssertions;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.Printers;

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

        var result = await handler.HandleAsync(
            new EditPrinterCommand(printer.Id, "Renamed", null, null, null, null, null),
            CancellationToken.None);

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
            new EditPrinterCommand(printer.Id, "Renamed", "10.0.0.99", "12345678", null, null, null),
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
            new EditPrinterCommand(printer.Id, "Renamed", "10.0.0.99", "wrong", null, null, null),
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
            new EditPrinterCommand(printer.Id, "Renamed", null, null, brand, modelCode, serialNumber),
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
            new EditPrinterCommand(printer.Id, "Renamed", null, null, printer.Brand, printer.ModelCode, printer.SerialNumber),
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

        var act = () => handler.HandleAsync(
            new EditPrinterCommand(Guid.NewGuid(), "Renamed", null, null, null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<PrinterNotFoundException>();
    }

    private static EditPrinterCommandHandler CreateHandler(
        InMemoryPrinterRepository repository,
        RecordingPrinterConnectorFactory connectorFactory,
        RecordingPrinterConnectionManager connectionManager) =>
        new(connectorFactory, repository, new InMemoryConnectionSecretRepository(), new PassthroughSecretProtector(), connectionManager);

    private static Printer CreatePrinter() =>
        Printer.Create(Guid.NewGuid(), "Printer 1", "Bambu", "P1S", "10.0.0.5", "SN-0001");

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
        }
    }
}
