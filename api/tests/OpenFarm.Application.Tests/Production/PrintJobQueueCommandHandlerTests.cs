using FluentAssertions;
using OpenFarm.Application.Production;
using OpenFarm.Application.Tests.Production.Fakes;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Tests.Production;

public class PinPrintJobCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidPrinter_PinsJob()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var printer = ProductionTestFixtures.CreateLiveIntegratedPrinter();
        var jobRepository = new InMemoryPrintJobRepository(job);
        var handler = new PinPrintJobCommandHandler(jobRepository, new InMemoryPrinterRepository(printer));

        var result = await handler.HandleAsync(new PinPrintJobCommand(job.Id, printer.Id), CancellationToken.None);

        result.AssignmentMode.Should().Be(AssignmentMode.Pinned);
        result.PinnedPrinterId.Should().Be(printer.Id);
    }

    [Fact]
    public async Task HandleAsync_MissingPrinterId_ThrowsPinnedJobRequiresPrinter()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var handler = new PinPrintJobCommandHandler(new InMemoryPrintJobRepository(job), new InMemoryPrinterRepository());

        var act = () => handler.HandleAsync(new PinPrintJobCommand(job.Id, null), CancellationToken.None);

        await act.Should().ThrowAsync<PinnedJobRequiresPrinterException>();
    }

    [Fact]
    public async Task HandleAsync_UnknownPrinter_ThrowsPrinterNotFound()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var handler = new PinPrintJobCommandHandler(new InMemoryPrintJobRepository(job), new InMemoryPrinterRepository());

        var act = () => handler.HandleAsync(new PinPrintJobCommand(job.Id, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<OpenFarm.Application.Printers.PrinterNotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_AlreadyDispatchedJob_ThrowsAlreadyDispatched()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        var printer = ProductionTestFixtures.CreateLiveIntegratedPrinter();
        job.Dispatch(printer.Id);
        var handler = new PinPrintJobCommandHandler(new InMemoryPrintJobRepository(job), new InMemoryPrinterRepository(printer));

        var act = () => handler.HandleAsync(new PinPrintJobCommand(job.Id, printer.Id), CancellationToken.None);

        await act.Should().ThrowAsync<PrintJobAlreadyDispatchedException>();
    }
}

public class UnpinPrintJobCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_PinnedJob_RevertsToUnassigned()
    {
        var job = ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        job.Pin(Guid.NewGuid());
        var handler = new UnpinPrintJobCommandHandler(new InMemoryPrintJobRepository(job));

        var result = await handler.HandleAsync(new UnpinPrintJobCommand(job.Id), CancellationToken.None);

        result.AssignmentMode.Should().Be(AssignmentMode.Unassigned);
        result.PinnedPrinterId.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_UnknownJob_ThrowsNotFound()
    {
        var handler = new UnpinPrintJobCommandHandler(new InMemoryPrintJobRepository());

        var act = () => handler.HandleAsync(new UnpinPrintJobCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<PrintJobNotFoundException>();
    }
}

public class ReorderPrintJobCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_AfterJobId_PlacesMidwayBetweenItAndItsSuccessor()
    {
        var requirementId = Guid.NewGuid();
        var first = ProductionTestFixtures.CreateQueuedJob(requirementId, queuePosition: 1024);
        var second = ProductionTestFixtures.CreateQueuedJob(requirementId, queuePosition: 2048);
        var moving = ProductionTestFixtures.CreateQueuedJob(requirementId, queuePosition: 4096);
        var jobRepository = new InMemoryPrintJobRepository(first, second, moving);
        var handler = new ReorderPrintJobCommandHandler(jobRepository);

        var result = await handler.HandleAsync(new ReorderPrintJobCommand(moving.Id, first.Id), CancellationToken.None);

        result.QueuePosition.Should().Be((1024 + 2048) / 2.0);
    }

    [Fact]
    public async Task HandleAsync_NullAfterJobId_MovesToTop()
    {
        var requirementId = Guid.NewGuid();
        var existing = ProductionTestFixtures.CreateQueuedJob(requirementId, queuePosition: 1024);
        var moving = ProductionTestFixtures.CreateQueuedJob(requirementId, queuePosition: 4096);
        var jobRepository = new InMemoryPrintJobRepository(existing, moving);
        var handler = new ReorderPrintJobCommandHandler(jobRepository);

        var result = await handler.HandleAsync(new ReorderPrintJobCommand(moving.Id, null), CancellationToken.None);

        result.QueuePosition.Should().BeLessThan(existing.QueuePosition);
    }

    [Fact]
    public async Task HandleAsync_UnknownJob_ThrowsNotFound()
    {
        var handler = new ReorderPrintJobCommandHandler(new InMemoryPrintJobRepository());

        var act = () => handler.HandleAsync(new ReorderPrintJobCommand(Guid.NewGuid(), null), CancellationToken.None);

        await act.Should().ThrowAsync<PrintJobNotFoundException>();
    }
}

public class RequeuePrintJobCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_CancelledFiniteJob_CreatesReplacementAndLinksBack()
    {
        var requirement = PartRequirement.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5);
        var part = ProductionTestFixtures.CreatePart();
        var requirementWithPart = PartRequirement.Create(Guid.NewGuid(), requirement.RunLineId, part.Id, 5);
        var job = ProductionTestFixtures.CreateQueuedJob(requirementWithPart.Id);
        job.MarkCancelled();

        var jobRepository = new InMemoryPrintJobRepository(job);
        var requirementRepository = new InMemoryPartRequirementRepository(requirementWithPart);
        var partRepository = new InMemoryPartRepository(part);
        var handler = new RequeuePrintJobCommandHandler(jobRepository, requirementRepository, partRepository);

        var replacement = await handler.HandleAsync(new RequeuePrintJobCommand(job.Id), CancellationToken.None);

        replacement.PartRequirementId.Should().Be(requirementWithPart.Id);
        replacement.Status.Should().Be(PrintJobStatus.Queued);
        job.RequeuedAsJobId.Should().Be(replacement.Id);
    }

    [Fact]
    public async Task HandleAsync_AlreadyRequeuedJob_Throws()
    {
        var requirement = PartRequirement.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5);
        var job = ProductionTestFixtures.CreateQueuedJob(requirement.Id);
        job.MarkCancelled();
        job.MarkRequeuedAs(Guid.NewGuid());

        var handler = new RequeuePrintJobCommandHandler(
            new InMemoryPrintJobRepository(job), new InMemoryPartRequirementRepository(requirement), new InMemoryPartRepository());

        var act = () => handler.HandleAsync(new RequeuePrintJobCommand(job.Id), CancellationToken.None);

        await act.Should().ThrowAsync<PrintJobAlreadyRequeuedException>();
    }

    [Fact]
    public async Task HandleAsync_StillQueuedJob_ThrowsNotEligibleForRequeue()
    {
        var requirement = PartRequirement.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5);
        var job = ProductionTestFixtures.CreateQueuedJob(requirement.Id);

        var handler = new RequeuePrintJobCommandHandler(
            new InMemoryPrintJobRepository(job), new InMemoryPartRequirementRepository(requirement), new InMemoryPartRepository());

        var act = () => handler.HandleAsync(new RequeuePrintJobCommand(job.Id), CancellationToken.None);

        await act.Should().ThrowAsync<PrintJobNotEligibleForRequeueException>();
    }

    [Fact]
    public async Task HandleAsync_BackfillLineJob_ThrowsNotEligibleForRequeue()
    {
        var requirement = PartRequirement.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);
        var job = ProductionTestFixtures.CreateQueuedJob(requirement.Id);
        job.MarkCancelled();

        var handler = new RequeuePrintJobCommandHandler(
            new InMemoryPrintJobRepository(job), new InMemoryPartRequirementRepository(requirement), new InMemoryPartRepository());

        var act = () => handler.HandleAsync(new RequeuePrintJobCommand(job.Id), CancellationToken.None);

        await act.Should().ThrowAsync<PrintJobNotEligibleForRequeueException>();
    }
}
