using FluentAssertions;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Production;
using OpenFarm.Application.Tests.Production.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Tests.Production;

public class CreateProductionRunCommandHandlerTests
{
    [Theory]
    [InlineData(RunLineTargetKind.Part)]
    [InlineData(RunLineTargetKind.Assembly)]
    public async Task IncompleteRequirements_RejectTheEntireRunBeforePersisting(RunLineTargetKind kind)
    {
        var part = ProductionTestFixtures.CreatePart();
        part.SetProcessingMetadata(Guid.NewGuid(), false, null);
        var runs = new InMemoryProductionRunRepository();
        var handler = new CreateProductionRunCommandHandler(runs, new InMemoryPartRequirementRepository(),
            new InMemoryPrintJobRepository(), new InMemoryPartRepository(part),
            new FakeAssemblyGraphQueryService(new BomExplosionResult([new ExplodedPartQuantity(part.Id, part.Name, 1)], [])));
        var command = new CreateProductionRunCommand("Incomplete", null, null,
            [new RunLineInput(kind, kind == RunLineTargetKind.Part ? part.Id : Guid.NewGuid(), 1, null, AssignmentMode.Unassigned, null, 1, null)]);
        var act = async () => await handler.HandleAsync(command, CancellationToken.None);
        await act.Should().ThrowAsync<PartPrintRequirementsIncompleteException>();
        (await runs.ListAsync(1, 25, CancellationToken.None)).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_PartLineWithFiniteQuantity_CreatesOneRequirementAndCeiledJobCount()
    {
        var part = ProductionTestFixtures.CreatePart(unitsPerPrint: 4);
        var jobRepository = new InMemoryPrintJobRepository();
        var requirementRepository = new InMemoryPartRequirementRepository();
        var runRepository = new InMemoryProductionRunRepository();
        var handler = new CreateProductionRunCommandHandler(
            runRepository, requirementRepository, jobRepository, new InMemoryPartRepository(part),
            new FakeAssemblyGraphQueryService(new BomExplosionResult([], [])));

        var lines = new List<RunLineInput>
        {
            new(RunLineTargetKind.Part, part.Id, 20, null, AssignmentMode.Unassigned, null, 1, null),
        };

        var run = await handler.HandleAsync(new CreateProductionRunCommand(null, null, null, lines), CancellationToken.None);

        run.Lines.Should().ContainSingle();
        var jobs = await jobRepository.ListByPartRequirementIdAsync(
            (await requirementRepository.ListByRunLineIdAsync(run.Lines[0].Id, CancellationToken.None)).Single().Id,
            CancellationToken.None);
        jobs.Should().HaveCount(5);
        jobs.Should().OnlyContain(j => j.Status == PrintJobStatus.Queued && j.AssignmentMode == AssignmentMode.Unassigned);
    }

    [Fact]
    public async Task HandleAsync_UnlimitedLine_CreatesRequirementButZeroJobs()
    {
        var part = ProductionTestFixtures.CreatePart();
        var jobRepository = new InMemoryPrintJobRepository();
        var requirementRepository = new InMemoryPartRequirementRepository();
        var handler = new CreateProductionRunCommandHandler(
            new InMemoryProductionRunRepository(), requirementRepository, jobRepository, new InMemoryPartRepository(part),
            new FakeAssemblyGraphQueryService(new BomExplosionResult([], [])));

        var lines = new List<RunLineInput>
        {
            new(RunLineTargetKind.Part, part.Id, null, null, AssignmentMode.Unassigned, null, 1, null),
        };

        var run = await handler.HandleAsync(new CreateProductionRunCommand(null, null, null, lines), CancellationToken.None);

        var requirement = (await requirementRepository.ListByRunLineIdAsync(run.Lines[0].Id, CancellationToken.None)).Single();
        requirement.Quantity.Should().BeNull();
        (await jobRepository.ListByPartRequirementIdAsync(requirement.Id, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_AssemblyLine_UsesGraphExplosionForRequirements()
    {
        var part = ProductionTestFixtures.CreatePart(unitsPerPrint: 1);
        var assemblyId = Guid.NewGuid();
        var handler = new CreateProductionRunCommandHandler(
            new InMemoryProductionRunRepository(), new InMemoryPartRequirementRepository(), new InMemoryPrintJobRepository(),
            new InMemoryPartRepository(part),
            new FakeAssemblyGraphQueryService(new BomExplosionResult([new ExplodedPartQuantity(part.Id, part.Name, 3)], [])));

        var lines = new List<RunLineInput>
        {
            new(RunLineTargetKind.Assembly, assemblyId, 1, null, AssignmentMode.Unassigned, null, 1, null),
        };

        var run = await handler.HandleAsync(new CreateProductionRunCommand(null, null, null, lines), CancellationToken.None);

        run.Lines[0].TargetKind.Should().Be(RunLineTargetKind.Assembly);
    }
}

public class CancelProductionRunCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_CancelsOnlyNeverDispatchedQueuedJobs()
    {
        var run = ProductionRun.Create(
            null, null, null,
            [new ProductionDemandSpec(RunLineTargetKind.Part, Guid.NewGuid(), 5, null, AssignmentMode.Unassigned, null, 1, null)]);
        var requirementId = Guid.NewGuid();

        var queuedJob = ProductionTestFixtures.CreateQueuedJob(requirementId);
        var dispatchedJob = ProductionTestFixtures.CreateQueuedJob(requirementId);
        dispatchedJob.Dispatch(Guid.NewGuid());

        var jobRepository = new InMemoryPrintJobRepository(queuedJob, dispatchedJob);
        jobRepository.JobsByRunId[run.Id] = [queuedJob, dispatchedJob];
        var runRepository = new InMemoryProductionRunRepository(run);
        var publisher = new RecordingProductionEventPublisher();
        var handler = new CancelProductionRunCommandHandler(runRepository, jobRepository, publisher);

        var result = await handler.HandleAsync(new CancelProductionRunCommand(run.Id), CancellationToken.None);

        result.Status.Should().Be(ProductionRunStatus.Cancelled);
        queuedJob.Status.Should().Be(PrintJobStatus.Cancelled);
        dispatchedJob.Status.Should().Be(PrintJobStatus.Queued);
        publisher.JobStatusChanges.Should().ContainSingle(c => c.JobId == queuedJob.Id);
    }

    [Fact]
    public async Task HandleAsync_UnknownRun_ThrowsNotFound()
    {
        var handler = new CancelProductionRunCommandHandler(
            new InMemoryProductionRunRepository(), new InMemoryPrintJobRepository(), new RecordingProductionEventPublisher());

        var act = () => handler.HandleAsync(new CancelProductionRunCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ProductionRunNotFoundException>();
    }
}

public class CloseProductionRunCommandHandlerTests
{
    private static ProductionDemandSpec Line(int? quantity) => new(RunLineTargetKind.Part, Guid.NewGuid(), quantity, null, AssignmentMode.Unassigned, null, 1, null);

    private static CloseProductionRunCommandHandler Handler(
        ProductionRun? run, RecordingProductionEventPublisher publisher, InMemoryPartRequirementRepository? requirements = null,
        InMemoryPrintJobRepository? jobs = null)
    {
        var runs = run is null ? new InMemoryProductionRunRepository() : new InMemoryProductionRunRepository(run);
        requirements ??= new InMemoryPartRequirementRepository();
        jobs ??= new InMemoryPrintJobRepository();
        return new CloseProductionRunCommandHandler(runs, jobs, new ProductionRunCompletionService(runs, requirements, jobs, publisher), publisher);
    }

    [Fact]
    public async Task HandleAsync_FromComplete_ClosesAndPublishes()
    {
        var run = ProductionRun.Create(null, null, null, [Line(1)]);
        run.MarkComplete();
        var publisher = new RecordingProductionEventPublisher();

        var result = await Handler(run, publisher).HandleAsync(new CloseProductionRunCommand(run.Id), CancellationToken.None);

        result.Status.Should().Be(ProductionRunStatus.Closed);
        publisher.RunStatusChanges.Should().ContainSingle(c => c.RunId == run.Id && c.Status == ProductionRunStatus.Closed);
    }

    [Fact]
    public async Task HandleAsync_ActiveWithFiniteWorkLeft_ThrowsInvalidRunCompletion()
    {
        var run = ProductionRun.Create(null, null, null, [Line(1)]);
        var requirement = PartRequirement.Create(run.Lines[0].Id, Guid.NewGuid(), 1);
        var jobs = new InMemoryPrintJobRepository(ProductionTestFixtures.CreateQueuedJob(requirement.Id));

        var act = () => Handler(run, new RecordingProductionEventPublisher(), new InMemoryPartRequirementRepository(requirement), jobs)
            .HandleAsync(new CloseProductionRunCommand(run.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRunCompletionException>();
    }

    [Fact]
    public async Task HandleAsync_ActiveUnlimitedOnly_ClosesAndCancelsItsUnsentJobs()
    {
        var run = ProductionRun.Create(null, null, null, [Line(null)]);
        var requirement = PartRequirement.Create(run.Lines[0].Id, Guid.NewGuid(), null);
        var unsent = ProductionTestFixtures.CreateQueuedJob(requirement.Id);
        var printing = ProductionTestFixtures.CreateQueuedJob(requirement.Id);
        printing.Dispatch(Guid.NewGuid());
        printing.MarkStarted();
        var jobs = new InMemoryPrintJobRepository(unsent, printing);
        jobs.JobsByRunId[run.Id] = [unsent, printing];

        var result = await Handler(run, new RecordingProductionEventPublisher(), new InMemoryPartRequirementRepository(requirement), jobs)
            .HandleAsync(new CloseProductionRunCommand(run.Id), CancellationToken.None);

        result.Status.Should().Be(ProductionRunStatus.Closed);
        unsent.Status.Should().Be(PrintJobStatus.Cancelled);
        printing.Status.Should().Be(PrintJobStatus.Printing);
    }

    [Fact]
    public async Task HandleAsync_UnknownRun_ThrowsNotFound()
    {
        var act = () => Handler(null, new RecordingProductionEventPublisher()).HandleAsync(new CloseProductionRunCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ProductionRunNotFoundException>();
    }
}
