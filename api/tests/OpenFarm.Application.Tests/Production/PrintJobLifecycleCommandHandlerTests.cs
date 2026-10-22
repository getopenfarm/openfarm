using FluentAssertions;
using OpenFarm.Application.Production;
using OpenFarm.Application.Tests.Production.Fakes;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Tests.Production;

public class CompletePrintJobCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_FromPrinting_LiveIntegratedJobRequiresTelemetry()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var printer = ProductionTestFixtures.CreateLiveIntegratedPrinter();
        job.Dispatch(printer.Id);
        job.MarkStarted();

        var jobRepository = new InMemoryPrintJobRepository(job);
        var printerRepository = new InMemoryPrinterRepository(printer);
        var runRepository = new InMemoryProductionRunRepository();
        var requirementRepository = new InMemoryPartRequirementRepository();
        var publisher = new RecordingProductionEventPublisher();
        var completionService = new ProductionRunCompletionService(runRepository, requirementRepository, jobRepository, publisher);
        var handler = new CompletePrintJobCommandHandler(
            jobRepository, printerRepository, ProductionTestFixtures.CreateDeduction(), publisher, completionService);

        var act = () => handler.HandleAsync(new CompletePrintJobCommand(job.Id), CancellationToken.None);
        await act.Should().ThrowAsync<JobNotEligibleForRemoteControlException>();
        job.Status.Should().Be(PrintJobStatus.Printing);
        publisher.JobStatusChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_FromQueued_ThrowsNotEligible()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var jobRepository = new InMemoryPrintJobRepository(job);
        var runRepository = new InMemoryProductionRunRepository();
        var requirementRepository = new InMemoryPartRequirementRepository();
        var publisher = new RecordingProductionEventPublisher();
        var completionService = new ProductionRunCompletionService(runRepository, requirementRepository, jobRepository, publisher);
        var handler = new CompletePrintJobCommandHandler(
            jobRepository, new InMemoryPrinterRepository(), ProductionTestFixtures.CreateDeduction(), publisher, completionService);

        var act = () => handler.HandleAsync(new CompletePrintJobCommand(job.Id), CancellationToken.None);

        await act.Should().ThrowAsync<JobNotEligibleForRemoteControlException>();
    }

    [Fact]
    public async Task HandleAsync_UnknownJob_ThrowsNotFound()
    {
        var jobRepository = new InMemoryPrintJobRepository();
        var runRepository = new InMemoryProductionRunRepository();
        var requirementRepository = new InMemoryPartRequirementRepository();
        var publisher = new RecordingProductionEventPublisher();
        var completionService = new ProductionRunCompletionService(runRepository, requirementRepository, jobRepository, publisher);
        var handler = new CompletePrintJobCommandHandler(
            jobRepository, new InMemoryPrinterRepository(), ProductionTestFixtures.CreateDeduction(), publisher, completionService);

        var act = () => handler.HandleAsync(new CompletePrintJobCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<PrintJobNotFoundException>();
    }
}

public class FailPrintJobCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_FromPrinting_LiveIntegratedJobRequiresTelemetry()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var printer = ProductionTestFixtures.CreateLiveIntegratedPrinter();
        job.Dispatch(printer.Id);
        job.MarkStarted();

        var jobRepository = new InMemoryPrintJobRepository(job);
        var printerRepository = new InMemoryPrinterRepository(printer);
        var publisher = new RecordingProductionEventPublisher();
        var handler = new FailPrintJobCommandHandler(jobRepository, printerRepository, ProductionTestFixtures.CreateDeduction(), publisher);

        var act = () => handler.HandleAsync(new FailPrintJobCommand(job.Id), CancellationToken.None);
        await act.Should().ThrowAsync<JobNotEligibleForRemoteControlException>();
        job.Status.Should().Be(PrintJobStatus.Printing);
    }

    [Fact]
    public async Task HandleAsync_FromQueued_ThrowsNotEligible()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var handler = new FailPrintJobCommandHandler(
            new InMemoryPrintJobRepository(job), new InMemoryPrinterRepository(),
            ProductionTestFixtures.CreateDeduction(),
            new RecordingProductionEventPublisher());

        var act = () => handler.HandleAsync(new FailPrintJobCommand(job.Id), CancellationToken.None);

        await act.Should().ThrowAsync<JobNotEligibleForRemoteControlException>();
    }
}

public class MarkPrintJobStartedCommandHandlerTests
{
    // No success-path test here: this handler's manual-brand branch (SupportsLiveIntegration ==
    // false) needs a Printer whose Brand isn't in PrinterModelCatalog — impossible to construct via
    // Printer.Create today, since every registered (Brand, ModelCode) entry is Bambu or System, both
    // live-integrated (PrinterModelCatalog's own remarks: "there is no non-live brand registrable
    // today... Phase 15 is what would ever add one"). Only the two rejection paths are reachable.

    [Fact]
    public async Task HandleAsync_LiveIntegratedPrinter_ThrowsNotEligible()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var printer = ProductionTestFixtures.CreateLiveIntegratedPrinter();
        job.Dispatch(printer.Id);

        var handler = new MarkPrintJobStartedCommandHandler(
            new InMemoryPrintJobRepository(job), new InMemoryPrinterRepository(printer), new RecordingProductionEventPublisher());

        var act = () => handler.HandleAsync(new MarkPrintJobStartedCommand(job.Id), CancellationToken.None);

        await act.Should().ThrowAsync<JobNotEligibleForRemoteControlException>();
    }

    [Fact]
    public async Task HandleAsync_NotYetDispatched_ThrowsNotEligible()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var handler = new MarkPrintJobStartedCommandHandler(
            new InMemoryPrintJobRepository(job), new InMemoryPrinterRepository(), new RecordingProductionEventPublisher());

        var act = () => handler.HandleAsync(new MarkPrintJobStartedCommand(job.Id), CancellationToken.None);

        await act.Should().ThrowAsync<JobNotEligibleForRemoteControlException>();
    }
}
