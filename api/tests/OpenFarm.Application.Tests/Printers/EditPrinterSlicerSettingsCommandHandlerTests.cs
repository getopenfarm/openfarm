using FluentAssertions;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Tests.Printers;

public class EditPrinterSlicerSettingsCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_CompatibleBuildPlate_PersistsAndLogsSelectedEvent()
    {
        var printer = CreatePrinter();
        var log = new RecordingActivityLogWriter();

        var result = await CreateHandler(printer, log).HandleAsync(Command(printer.Id, BuildPlateType.TexturedPei), CancellationToken.None);

        result.BuildPlateType.Should().Be(BuildPlateType.TexturedPei);
        log.Entries.Should().ContainSingle(e => e.EventType == "PrinterBuildPlateSelected");
    }

    [Fact]
    public async Task HandleAsync_IncompatibleBuildPlate_ThrowsAndLeavesPrinterUnchanged()
    {
        // P1S plates are {Cool, HighTemp, TexturedPei, Supertack}; Engineering is not one of them.
        var printer = CreatePrinter();

        var act = () => CreateHandler(printer).HandleAsync(Command(printer.Id, BuildPlateType.Engineering), CancellationToken.None);

        await act.Should().ThrowAsync<IncompatibleBuildPlateException>();
        printer.BuildPlateType.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ClearingBuildPlate_LogsClearedEvent()
    {
        var printer = CreatePrinter();
        printer.SelectBuildPlate(BuildPlateType.HighTemp);
        var log = new RecordingActivityLogWriter();

        var result = await CreateHandler(printer, log).HandleAsync(Command(printer.Id, null), CancellationToken.None);

        result.BuildPlateType.Should().BeNull();
        log.Entries.Should().ContainSingle(e => e.EventType == "PrinterBuildPlateCleared");
    }

    [Fact]
    public async Task HandleAsync_ResendingTheSamePlate_LogsNothing()
    {
        var printer = CreatePrinter();
        printer.SelectBuildPlate(BuildPlateType.HighTemp);
        var log = new RecordingActivityLogWriter();

        await CreateHandler(printer, log).HandleAsync(Command(printer.Id, BuildPlateType.HighTemp), CancellationToken.None);

        log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhilePrinting_ChangesThePlate()
    {
        var printer = CreatePrinter();
        printer.SelectBuildPlate(BuildPlateType.HighTemp);
        printer.ObserveStatus("RUNNING");

        var result = await CreateHandler(printer).HandleAsync(Command(printer.Id, BuildPlateType.TexturedPei), CancellationToken.None);

        result.BuildPlateType.Should().Be(BuildPlateType.TexturedPei);
        result.Status.Should().Be(PrinterStatus.Printing);
    }

    [Fact]
    public async Task HandleAsync_UnknownPrinterId_ThrowsPrinterNotFound()
    {
        var act = () => CreateHandler().HandleAsync(Command(Guid.NewGuid(), BuildPlateType.HighTemp), CancellationToken.None);

        await act.Should().ThrowAsync<PrinterNotFoundException>();
    }

    private static EditPrinterSlicerSettingsCommand Command(Guid printerId, BuildPlateType? buildPlateType) =>
        new(printerId, buildPlateType, null, ActorType.System);

    private static EditPrinterSlicerSettingsCommandHandler CreateHandler(Printer? printer = null, RecordingActivityLogWriter? log = null) =>
        new(new InMemoryPrinterRepository(printer), log ?? new RecordingActivityLogWriter());

    private static Printer CreatePrinter() => Printer.Create("Printer 1", "Bambu", "P1S", "10.0.0.5", "SN-0001");

    private sealed class InMemoryPrinterRepository(Printer? printer) : IPrinterRepository
    {
        public Task AddAsync(Printer p, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Printer?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(printer is not null && printer.Id == id ? printer : null);

        public Task<Printer?> GetBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Printer>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RemoveAsync(Printer p, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
