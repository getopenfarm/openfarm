using FluentAssertions;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Tests.Printers;

public class RegisterPrinterCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_BlankSerial_RegistersUnderTheSerialThePrinterReports()
    {
        var printers = new InMemoryPrinterRepository();
        var secrets = new InMemoryConnectionSecretRepository();
        var connector = new SerialReportingConnector("SNAP-U1-0042");

        var printer = await CreateHandler(printers, secrets, connector).HandleAsync(Snapmaker(), CancellationToken.None);

        printer.SerialNumber.Should().Be("SNAP-U1-0042");
        printers.Printers.Should().ContainSingle(p => p.SerialNumber == "SNAP-U1-0042");
        secrets.Secrets.Should().ContainSingle().Which.PrinterSerialNumber.Should().Be("SNAP-U1-0042");
        connector.VerifiedSerials.Should().Equal("");
    }

    [Fact]
    public async Task HandleAsync_ReportedSerialAlreadyRegistered_ThrowsDuplicate()
    {
        var printers = new InMemoryPrinterRepository(Printer.Create("Existing", "Snapmaker", "U1", "10.0.0.7", "SNAP-U1-0042"));
        var secrets = new InMemoryConnectionSecretRepository();

        var act = () => CreateHandler(printers, secrets, new SerialReportingConnector("SNAP-U1-0042")).HandleAsync(Snapmaker(), CancellationToken.None);

        await act.Should().ThrowAsync<DuplicatePrinterSerialNumberException>();
        printers.Printers.Should().ContainSingle();
        secrets.Secrets.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_BlankSerialThePrinterDoesNotReport_ThrowsAndSavesNothing()
    {
        var printers = new InMemoryPrinterRepository();
        var secrets = new InMemoryConnectionSecretRepository();

        var act = () => CreateHandler(printers, secrets, new SerialReportingConnector(null)).HandleAsync(Snapmaker(), CancellationToken.None);

        await act.Should().ThrowAsync<PrinterConnectionVerificationFailedException>();
        printers.Printers.Should().BeEmpty();
        secrets.Secrets.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_TypedSerial_KeepsItAndNeverAsksThePrinter()
    {
        var printers = new InMemoryPrinterRepository();
        var connector = new SerialReportingConnector("IGNORED");

        var printer = await CreateHandler(printers, new InMemoryConnectionSecretRepository(), connector).HandleAsync(
            new RegisterPrinterCommand("P1S", "Bambu", "P1S", "10.0.0.8", "01P00A123456789", "12345678"), CancellationToken.None);

        printer.SerialNumber.Should().Be("01P00A123456789");
        connector.SerialReads.Should().Be(0);
        connector.VerifiedSerials.Should().Equal("01P00A123456789");
    }

    private static RegisterPrinterCommand Snapmaker() => new("U1", "Snapmaker", "U1", "10.0.0.9", "", "");

    private static RegisterPrinterCommandHandler CreateHandler(
        InMemoryPrinterRepository printers, InMemoryConnectionSecretRepository secrets, SerialReportingConnector connector) =>
        new(new SingleConnectorFactory(connector), printers, secrets, new PassthroughSecretProtector(), new NoOpConnectionManager());

    private sealed class SerialReportingConnector(string? serialNumber) : IPrinterConnector
    {
        public List<string> VerifiedSerials { get; } = [];

        public int SerialReads { get; private set; }

        public Task<bool> VerifyAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
        {
            VerifiedSerials.Add(details.SerialNumber);
            return Task.FromResult(true);
        }

        public Task<string?> ReadSerialNumberAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
        {
            SerialReads++;
            return Task.FromResult(serialNumber);
        }

        public IAsyncEnumerable<PrinterConnectorEvent> ConnectAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, FilamentFeedMapping? feedMapping, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class SingleConnectorFactory(IPrinterConnector connector) : IPrinterConnectorFactory
    {
        public IPrinterConnector CreateConnector(PrinterConnectionDetails details) => connector;

        public IPrinterCameraRelay CreateCameraRelay(PrinterConnectionDetails details) => throw new NotSupportedException();
    }

    private sealed class InMemoryPrinterRepository(params Printer[] printers) : IPrinterRepository
    {
        public List<Printer> Printers { get; } = [.. printers];

        public Task AddAsync(Printer printer, CancellationToken cancellationToken)
        {
            Printers.Add(printer);
            return Task.CompletedTask;
        }

        public Task<Printer?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Printers.FirstOrDefault(p => p.Id == id));

        public Task<Printer?> GetBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken) =>
            Task.FromResult(Printers.FirstOrDefault(p => p.SerialNumber == serialNumber));

        public Task<IReadOnlyList<Printer>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Printer>>(Printers);

        public Task RemoveAsync(Printer printer, CancellationToken cancellationToken)
        {
            Printers.Remove(printer);
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

    // Protect("") must still store a secret: the connection loop needs one for every printer.
    private sealed class PassthroughSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => $"protected:{plaintext}";

        public string Unprotect(string ciphertext) => ciphertext["protected:".Length..];
    }

    private sealed class NoOpConnectionManager : IPrinterConnectionManager
    {
        public void StartConnection(Guid printerId, CancellationToken cancellationToken)
        {
        }

        public void StopConnection(Guid printerId)
        {
        }
    }
}
