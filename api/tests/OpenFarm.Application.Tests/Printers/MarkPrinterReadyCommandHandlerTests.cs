using FluentAssertions;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Tests.Printers;

public class MarkPrinterReadyCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_FromAwaitingClear_TransitionsToIdleAndPublishesStatusChanged()
    {
        var printer = CreatePrinter();
        printer.ObserveStatus("FINISH");
        var repository = new InMemoryPrinterRepository(printer);
        var publisher = new RecordingPrinterEventPublisher();
        var dispatcher = new RecordingProductionDispatcher();
        var handler = new MarkPrinterReadyCommandHandler(repository, publisher, dispatcher);

        var result = await handler.HandleAsync(new MarkPrinterReadyCommand(printer.Id), CancellationToken.None);

        result.Status.Should().Be(PrinterStatus.Idle);
        publisher.PublishedStatuses.Should().ContainSingle(s => s.PrinterId == printer.Id && s.Status == PrinterStatus.Idle);
        dispatcher.RequestedPrinterIds.Should().ContainSingle(id => id == printer.Id);
    }

    [Theory]
    [InlineData("IDLE")]
    [InlineData("RUNNING")]
    public async Task HandleAsync_FromNonAwaitingClearStatus_ThrowsInvalidTransitionAndPublishesNothing(string gcodeState)
    {
        var printer = CreatePrinter();
        printer.ObserveStatus(gcodeState);
        var repository = new InMemoryPrinterRepository(printer);
        var publisher = new RecordingPrinterEventPublisher();
        var handler = new MarkPrinterReadyCommandHandler(repository, publisher, new RecordingProductionDispatcher());

        var act = () => handler.HandleAsync(new MarkPrinterReadyCommand(printer.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidPrinterStatusTransitionException>();
        publisher.PublishedStatuses.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WithUnknownPrinterId_ThrowsPrinterNotFound()
    {
        var repository = new InMemoryPrinterRepository();
        var publisher = new RecordingPrinterEventPublisher();
        var handler = new MarkPrinterReadyCommandHandler(repository, publisher, new RecordingProductionDispatcher());

        var act = () => handler.HandleAsync(new MarkPrinterReadyCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<PrinterNotFoundException>();
    }

    private static Printer CreatePrinter() =>
        Printer.Create(Guid.NewGuid(), "Test Printer", "Bambu", "X1C", "10.0.0.5", $"SN-{Guid.NewGuid():N}");

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

    private sealed class RecordingPrinterEventPublisher : IPrinterEventPublisher
    {
        public List<(Guid PrinterId, PrinterStatus Status, bool IsReachable)> PublishedStatuses { get; } = [];

        public Task PrinterStatusChangedAsync(
            Guid organizationId,
            Guid printerId,
            PrinterStatus status,
            bool isReachable,
            CancellationToken cancellationToken)
        {
            PublishedStatuses.Add((printerId, status, isReachable));
            return Task.CompletedTask;
        }

        public Task PrinterTelemetryUpdatedAsync(
            Guid organizationId,
            Guid printerId,
            PrinterTelemetrySnapshot telemetry,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CameraFrameReceivedAsync(
            Guid organizationId,
            Guid printerId,
            string frameJpegBase64,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingProductionDispatcher : IProductionDispatcher
    {
        public List<Guid> RequestedPrinterIds { get; } = [];

        public void RequestDispatchAttempt(Guid printerId) => RequestedPrinterIds.Add(printerId);
    }
}
