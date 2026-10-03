using FluentAssertions;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Application.Tests.Production.Fakes;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Tests.Production;

public class CancelPrintJobCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_QueuedJob_CancelsImmediatelyWithoutContactingAnyPrinter()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var jobRepository = new InMemoryPrintJobRepository(job);
        var publisher = new RecordingProductionEventPublisher();
        var connector = new RecordingPrinterConnector();
        var handler = new CancelPrintJobCommandHandler(
            jobRepository, new InMemoryPrinterRepository(), MakeDetailsResolver(), new RecordingPrinterConnectorFactory(connector), publisher);

        var result = await handler.HandleAsync(new CancelPrintJobCommand(job.Id), CancellationToken.None);

        result.Status.Should().Be(PrintJobStatus.Cancelled);
        connector.StopCalls.Should().Be(0);
        publisher.JobStatusChanges.Should().ContainSingle(c => c.JobId == job.Id && c.Status == PrintJobStatus.Cancelled);
    }

    [Fact]
    public async Task HandleAsync_PrintingJobOnLiveIntegratedPrinter_SendsStopAndOnlyRequestsCancel()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var printer = ProductionTestFixtures.CreateLiveIntegratedPrinter();
        job.Dispatch(printer.Id);
        job.MarkStarted();

        var jobRepository = new InMemoryPrintJobRepository(job);
        var printerRepository = new InMemoryPrinterRepository(printer);
        var connector = new RecordingPrinterConnector();
        var handler = new CancelPrintJobCommandHandler(
            jobRepository, printerRepository, MakeDetailsResolver(printer.SerialNumber), new RecordingPrinterConnectorFactory(connector),
            new RecordingProductionEventPublisher());

        var result = await handler.HandleAsync(new CancelPrintJobCommand(job.Id), CancellationToken.None);

        connector.StopCalls.Should().Be(1);
        result.CancelRequestedAtUtc.Should().NotBeNull();
        result.Status.Should().Be(PrintJobStatus.Printing);
    }

    [Fact]
    public async Task HandleAsync_TerminalJob_ThrowsNotEligible()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        job.MarkCancelled();
        var handler = new CancelPrintJobCommandHandler(
            new InMemoryPrintJobRepository(job), new InMemoryPrinterRepository(), MakeDetailsResolver(),
            new RecordingPrinterConnectorFactory(new RecordingPrinterConnector()), new RecordingProductionEventPublisher());

        var act = () => handler.HandleAsync(new CancelPrintJobCommand(job.Id), CancellationToken.None);

        await act.Should().ThrowAsync<JobNotEligibleForRemoteControlException>();
    }

    [Fact]
    public async Task HandleAsync_UnknownJob_ThrowsNotFound()
    {
        var handler = new CancelPrintJobCommandHandler(
            new InMemoryPrintJobRepository(), new InMemoryPrinterRepository(), MakeDetailsResolver(),
            new RecordingPrinterConnectorFactory(new RecordingPrinterConnector()), new RecordingProductionEventPublisher());

        var act = () => handler.HandleAsync(new CancelPrintJobCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<PrintJobNotFoundException>();
    }

    private static ResolvePrinterConnectionDetails MakeDetailsResolver(string? serialNumber = null) =>
        new(
            new InMemoryConnectionSecretRepository(
                serialNumber is null ? [] : [ConnectionSecret.Create(Guid.NewGuid(), serialNumber, "encrypted")]),
            new PassthroughSecretProtector());
}

public class PausePrintJobCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_PrintingJobOnLiveIntegratedPrinter_SendsPauseWithoutTouchingStatus()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var printer = ProductionTestFixtures.CreateLiveIntegratedPrinter();
        job.Dispatch(printer.Id);
        job.MarkStarted();

        var jobRepository = new InMemoryPrintJobRepository(job);
        var printerRepository = new InMemoryPrinterRepository(printer);
        var detailsResolver = new ResolvePrinterConnectionDetails(
            new InMemoryConnectionSecretRepository(ConnectionSecret.Create(Guid.NewGuid(), printer.SerialNumber, "encrypted")),
            new PassthroughSecretProtector());
        var resolver = new ProductionRemoteControlResolver(jobRepository, printerRepository, detailsResolver);
        var connector = new RecordingPrinterConnector();
        var handler = new PausePrintJobCommandHandler(
            resolver, new RecordingPrinterConnectorFactory(connector), jobRepository, new RecordingProductionEventPublisher());

        var result = await handler.HandleAsync(new PausePrintJobCommand(job.Id), CancellationToken.None);

        connector.PauseCalls.Should().Be(1);
        result.Status.Should().Be(PrintJobStatus.Printing);
    }

    [Fact]
    public async Task HandleAsync_JobNotPrinting_ThrowsNotEligible()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var jobRepository = new InMemoryPrintJobRepository(job);
        var printerRepository = new InMemoryPrinterRepository();
        var detailsResolver = new ResolvePrinterConnectionDetails(new InMemoryConnectionSecretRepository(), new PassthroughSecretProtector());
        var resolver = new ProductionRemoteControlResolver(jobRepository, printerRepository, detailsResolver);
        var handler = new PausePrintJobCommandHandler(
            resolver, new RecordingPrinterConnectorFactory(new RecordingPrinterConnector()), jobRepository,
            new RecordingProductionEventPublisher());

        var act = () => handler.HandleAsync(new PausePrintJobCommand(job.Id), CancellationToken.None);

        await act.Should().ThrowAsync<JobNotEligibleForRemoteControlException>();
    }
}

public class ResumePrintJobCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_PausedJobOnLiveIntegratedPrinter_SendsResumeWithoutTouchingStatus()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var printer = ProductionTestFixtures.CreateLiveIntegratedPrinter();
        job.Dispatch(printer.Id);
        job.MarkStarted();
        job.MarkPaused();

        var jobRepository = new InMemoryPrintJobRepository(job);
        var printerRepository = new InMemoryPrinterRepository(printer);
        var detailsResolver = new ResolvePrinterConnectionDetails(
            new InMemoryConnectionSecretRepository(ConnectionSecret.Create(Guid.NewGuid(), printer.SerialNumber, "encrypted")),
            new PassthroughSecretProtector());
        var resolver = new ProductionRemoteControlResolver(jobRepository, printerRepository, detailsResolver);
        var connector = new RecordingPrinterConnector();
        var handler = new ResumePrintJobCommandHandler(
            resolver, new RecordingPrinterConnectorFactory(connector), jobRepository, new RecordingProductionEventPublisher());

        var result = await handler.HandleAsync(new ResumePrintJobCommand(job.Id), CancellationToken.None);

        connector.ResumeCalls.Should().Be(1);
        result.Status.Should().Be(PrintJobStatus.Paused);
    }

    [Fact]
    public async Task HandleAsync_JobNotPaused_ThrowsNotEligible()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var jobRepository = new InMemoryPrintJobRepository(job);
        var printerRepository = new InMemoryPrinterRepository();
        var detailsResolver = new ResolvePrinterConnectionDetails(new InMemoryConnectionSecretRepository(), new PassthroughSecretProtector());
        var resolver = new ProductionRemoteControlResolver(jobRepository, printerRepository, detailsResolver);
        var handler = new ResumePrintJobCommandHandler(
            resolver, new RecordingPrinterConnectorFactory(new RecordingPrinterConnector()), jobRepository,
            new RecordingProductionEventPublisher());

        var act = () => handler.HandleAsync(new ResumePrintJobCommand(job.Id), CancellationToken.None);

        await act.Should().ThrowAsync<JobNotEligibleForRemoteControlException>();
    }
}
