using FluentAssertions;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Application.Tests.Production.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Tests.Production;

// Every run completes on its own, a lost plate resolves by reprint or write-off, and a reprint retries in place.
public class RunLifecycleCommandHandlerTests
{
    private sealed record Fixture(
        ProductionRun Run, PartRequirement Requirement, PrintJob Job, Part Part, InMemoryProductionRunRepository Runs,
        InMemoryPartRequirementRepository Requirements, InMemoryPrintJobRepository Jobs, RecordingProductionEventPublisher Publisher)
    {
        public ProductionRunCompletionService Completion => new(Runs, Requirements, Jobs, Publisher);

        public RequeuePrintJobCommandHandler Requeue => new(Jobs, Requirements, Runs, new InMemoryPrinterRepository());
    }

    /// <summary>A named finite run of one plate, its job snapshotted and waiting at queue position 5000.</summary>
    private static Fixture OnePlateRun(AssignmentMode mode = AssignmentMode.Unassigned, Guid? pinnedPrinterId = null)
    {
        var part = ProductionTestFixtures.CreatePart();
        var run = ProductionRun.Create("Order #1042", null, null,
            [new RunLineSpec(RunLineTargetKind.Part, part.Id, 1, null, AssignmentMode.Unassigned, null, 1, null)]);
        var requirement = PartRequirement.Create(run.Lines[0].Id, part.Id, 1);
        var job = PrintJob.Create(part.Name, requirement.Id, 5000, part.UnitsPerPrint, mode, pinnedPrinterId,
            part.RevisionHash, part.RevisionNumber, ProductionJobRequirements.FromPart(part));
        job.CaptureProductionSnapshot(part);
        return new Fixture(run, requirement, job, part, new InMemoryProductionRunRepository(run),
            new InMemoryPartRequirementRepository(requirement), new InMemoryPrintJobRepository(job), new RecordingProductionEventPublisher());
    }

    [Fact]
    public async Task ANamedRun_CompletesOnItsOwn_WhenItsLastPlateCompletes()
    {
        var f = OnePlateRun();
        f.Job.MarkStarted();
        f.Job.MarkCompleted();

        await f.Completion.TryCompleteRunForRequirementAsync(f.Requirement.Id, default);

        f.Run.Status.Should().Be(ProductionRunStatus.Complete);
        f.Publisher.RunStatusChanges.Should().ContainSingle(c => c.Status == ProductionRunStatus.Complete);
    }

    [Fact]
    public async Task WritingOffTheLastLostPlate_CompletesTheRun()
    {
        var f = OnePlateRun();
        f.Job.MarkCancelled();
        var handler = new WriteOffPrintJobCommandHandler(f.Jobs, f.Requirements, f.Completion, f.Publisher);

        await handler.HandleAsync(new WriteOffPrintJobCommand(f.Job.Id), default);

        f.Job.WrittenOffAtUtc.Should().NotBeNull();
        f.Run.Status.Should().Be(ProductionRunStatus.Complete);
    }

    [Fact]
    public async Task WriteOff_WorksWithoutASnapshot_SoAnUnrequeueablePlateNeverHoldsTheRunOpen()
    {
        var f = OnePlateRun();
        var legacy = ProductionTestFixtures.CreateQueuedJob(f.Requirement.Id);
        legacy.MarkCancelled();
        await f.Jobs.AddAsync(legacy, default);
        f.Job.MarkCancelled();
        var handler = new WriteOffPrintJobCommandHandler(f.Jobs, f.Requirements, f.Completion, f.Publisher);

        await handler.HandleAsync(new WriteOffPrintJobCommand(legacy.Id), default);
        await handler.HandleAsync(new WriteOffPrintJobCommand(f.Job.Id), default);

        f.Run.Status.Should().Be(ProductionRunStatus.Complete);
    }

    [Fact]
    public async Task Requeue_RetriesInPlace()
    {
        var f = OnePlateRun();
        f.Job.MarkStarted();
        f.Job.MarkFailed();

        var replacement = await f.Requeue.HandleAsync(new RequeuePrintJobCommand(f.Job.Id), default);

        replacement.QueuePosition.Should().Be(5000);
        f.Job.RequeuedAsJobId.Should().Be(replacement.Id);
    }

    [Fact]
    public async Task Requeue_DropsAPinToAPrinterThatNoLongerExists()
    {
        var f = OnePlateRun(AssignmentMode.Pinned, Guid.NewGuid());
        f.Job.MarkCancelled();

        var replacement = await f.Requeue.HandleAsync(new RequeuePrintJobCommand(f.Job.Id), default);

        replacement.AssignmentMode.Should().Be(AssignmentMode.Unassigned);
        replacement.PinnedPrinterId.Should().BeNull();
    }

    [Fact]
    public async Task Requeue_IsRefusedOnceTheRunIsCancelled()
    {
        var f = OnePlateRun();
        f.Job.MarkCancelled();
        f.Run.Cancel();

        var act = () => f.Requeue.HandleAsync(new RequeuePrintJobCommand(f.Job.Id), default);

        await act.Should().ThrowAsync<ProductionRunNotOpenException>();
        f.Job.RequeuedAsJobId.Should().BeNull();
    }

    [Fact]
    public async Task Requeue_IsRefusedForAWrittenOffPlate()
    {
        var f = OnePlateRun();
        f.Job.MarkCancelled();
        f.Job.WriteOff();

        var act = () => f.Requeue.HandleAsync(new RequeuePrintJobCommand(f.Job.Id), default);

        await act.Should().ThrowAsync<PrintJobNotEligibleForRequeueException>();
    }

    [Fact]
    public async Task Create_RejectsAPinToAnUnknownPrinter()
    {
        var part = ProductionTestFixtures.CreatePart();
        var runs = new InMemoryProductionRunRepository();
        var handler = new CreateProductionRunCommandHandler(runs, new InMemoryPartRequirementRepository(), new InMemoryPrintJobRepository(),
            new InMemoryPartRepository(part), new FakeAssemblyGraphQueryService(new BomExplosionResult([], [])),
            printerRepository: new InMemoryPrinterRepository());
        var printerId = Guid.NewGuid();

        var act = () => handler.HandleAsync(new CreateProductionRunCommand(null, null, null,
            [new RunLineInput(RunLineTargetKind.Part, part.Id, 1, null, AssignmentMode.Pinned, printerId, 1, null)]), default);

        await act.Should().ThrowAsync<PrinterNotFoundException>();
        (await runs.ListAsync(1, 25, default)).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_RejectsAnAssemblyWithNoPrintedParts()
    {
        var handler = new CreateProductionRunCommandHandler(new InMemoryProductionRunRepository(), new InMemoryPartRequirementRepository(),
            new InMemoryPrintJobRepository(), new InMemoryPartRepository(), new FakeAssemblyGraphQueryService(new BomExplosionResult([], [])));

        var act = () => handler.HandleAsync(new CreateProductionRunCommand(null, null, null,
            [new RunLineInput(RunLineTargetKind.Assembly, Guid.NewGuid(), 2, null, AssignmentMode.Unassigned, null, 1, null)]), default);

        await act.Should().ThrowAsync<RunLineHasNoPrintedPartsException>();
    }
}
