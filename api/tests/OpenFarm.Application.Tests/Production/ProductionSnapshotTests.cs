using FluentAssertions;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Production;
using OpenFarm.Application.Tests.Production.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Tests.Production;

public class ProductionSnapshotTests
{
    [Fact]
    public async Task FiniteCreationAndRetry_PreserveInputsWithoutLibraryPart()
    {
        var part = ProductionTestFixtures.CreatePart(unitsPerPrint: 4, revisionHash: new string('a', 64));
        var jobs = new InMemoryPrintJobRepository();
        var requirements = new InMemoryPartRequirementRepository();
        var runs = new InMemoryProductionRunRepository();
        var parts = new InMemoryPartRepository(part);
        var handler = new CreateProductionRunCommandHandler(runs, requirements, jobs, parts,
            new FakeAssemblyGraphQueryService(new BomExplosionResult([], [])));
        var run = await handler.HandleAsync(new CreateProductionRunCommand(null, null, null,
            [new RunLineInput(RunLineTargetKind.Part, part.Id, 5, null, AssignmentMode.Unassigned, null, 1, null)]), default);
        var requirement = (await requirements.ListByRunLineIdAsync(run.Lines[0].Id, default)).Single();
        var queued = await jobs.ListByPartRequirementIdAsync(requirement.Id, default);
        queued.Should().HaveCount(2);
        part.SetUnitsPerPrint(9);
        part.ReplaceFile(PartFileFormat.Stl, true, "new.stl", "new-path", 100, 0.6m,
            SlicerEngine.OrcaSlicer, "2.2.0", EstimateStatus.Unavailable, null, null, 1,
            new string('b', 64), [new FilamentRequirementSpec("PETG", "FF0000", null, null, null)], []);
        await parts.RemoveAsync(part, default);
        var job = queued[0];
        var snapshot = job.GetProductionPart()!;
        snapshot.Id.Should().Be(part.Id);
        snapshot.Name.Should().Be("Test part");
        snapshot.StoragePath.Should().Be("path");
        snapshot.NozzleDiameterMm.Should().Be(0.4m);
        snapshot.RequiresSlicing.Should().BeFalse();
        snapshot.UnitsPerPrint.Should().Be(4);
        snapshot.FilamentRequirements.Single().MaterialType.Should().Be("PLA");
        job.MarkCancelled();
        var retry = await new RequeuePrintJobCommandHandler(jobs, requirements, runs, new InMemoryPrinterRepository())
            .HandleAsync(new RequeuePrintJobCommand(job.Id), default);
        retry.ProductionSnapshotJson.Should().Be(job.ProductionSnapshotJson);
        retry.PlateQuantity.Should().Be(4);
        retry.FileRevisionHash.Should().Be(new string('a', 64));
        requirement.PartName.Should().Be("Test part");
        run.Lines.Single().TargetName.Should().Be("Test part");
    }

    [Fact]
    public async Task UnresolvedLegacyJob_CannotBeRequeuedFromLatestPart()
    {
        var part = ProductionTestFixtures.CreatePart();
        var requirement = PartRequirement.Create(Guid.NewGuid(), part.Id, 5);
        var job = ProductionTestFixtures.CreateQueuedJob(requirement.Id);
        job.MarkCancelled();
        var handler = new RequeuePrintJobCommandHandler(new InMemoryPrintJobRepository(job),
            new InMemoryPartRequirementRepository(requirement), new InMemoryProductionRunRepository(), new InMemoryPrinterRepository());
        var act = () => handler.HandleAsync(new RequeuePrintJobCommand(job.Id), default);
        await act.Should().ThrowAsync<ProductionSnapshotUnavailableException>();
    }

    [Fact]
    public void UnlimitedInputs_FollowLibraryUntilDispatchedThenUseSnapshot()
    {
        var part = ProductionTestFixtures.CreatePart();
        var requirement = PartRequirement.Create(Guid.NewGuid(), part.Id, null);
        var job = PrintJob.Create("Test part", requirement.Id, 1024, 1, AssignmentMode.Unassigned,
            null, part.RevisionHash, part.RevisionNumber, ProductionJobRequirements.FromPart(part));
        job.CaptureProductionSnapshot(part);
        part.SetUnitsPerPrint(7);
        ProductionJobRequirements.ResolvePart(job, requirement, part)!.UnitsPerPrint.Should().Be(7);
        job.Dispatch(Guid.NewGuid());
        ProductionJobRequirements.ResolvePart(job, requirement, null)!.UnitsPerPrint.Should().Be(1);
    }

    [Theory]
    [InlineData(5, RevisionMode.FollowLatest)]
    [InlineData(null, RevisionMode.Snapshot)]
    public void Quantity_RejectsConflictingRevisionModes(int? quantity, RevisionMode mode)
    {
        var act = () => ProductionRun.Create(null, null, null,
            [new RunLineSpec(RunLineTargetKind.Part, Guid.NewGuid(), quantity, mode, AssignmentMode.Unassigned, null, 1, null)]);
        act.Should().Throw<InvalidRevisionModeException>();
    }
    [Theory]
    [InlineData(PartFileFormat.Stl)]
    [InlineData(PartFileFormat.ThreeMf)]
    public void UnslicedSnapshot_RetainsResolvedSlicerAndSourceMetadata(PartFileFormat format)
    {
        var part = Part.Create("Unsliced", format, true, "source.3mf", "retained-source",
            100, 0.4m, null, null, EstimateStatus.Unavailable, null, null, 1, 2, new string('a', 64),
            [new FilamentRequirementSpec("PLA", null, null, null, null)], [], []);
        var job = PrintJob.Create("Test part", Guid.NewGuid(), 1024, 2, AssignmentMode.Unassigned,
            null, part.RevisionHash, part.RevisionNumber, ProductionJobRequirements.FromPart(part));
        job.CaptureProductionSnapshot(part, new SlicerRef(SlicerEngine.OrcaSlicer, "2.2.0"));
        part.ReplaceFile(PartFileFormat.GCode, false, "replacement.gcode", "replacement-path", 20,
            0.6m, SlicerEngine.BambuStudio, "other", EstimateStatus.Available, 10, 5, 1, new string('b', 64),
            [new FilamentRequirementSpec("PETG", null, null, null, null)], []);
        var source = job.GetProductionPart()!;
        source.FileFormat.Should().Be(format);
        source.RequiresSlicing.Should().BeTrue();
        source.StoragePath.Should().Be("retained-source");
        source.SlicerEngineOverride.Should().Be(SlicerEngine.OrcaSlicer);
        source.SlicerVersionUsed.Should().Be("2.2.0");
        source.EstimateStatus.Should().Be(EstimateStatus.Unavailable);
        source.EstimatedPrintTimeMinutes.Should().BeNull();
        source.NozzleDiameterMm.Should().Be(0.4m);
    }

    [Fact]
    public void RemovingLegacySource_IsPermanentAndCannotBeReplacedByAValidSnapshot()
    {
        var part = ProductionTestFixtures.CreatePart();
        var job = PrintJob.Create("Test part", Guid.NewGuid(), 1024, part.UnitsPerPrint,
            AssignmentMode.Unassigned, null, part.RevisionHash, part.RevisionNumber, ProductionJobRequirements.FromPart(part));
        job.MarkCancelled();
        var removedAt = DateTimeOffset.UtcNow;
        job.MarkSnapshotUnavailable(removedAt);
        job.MarkSnapshotUnavailable(removedAt.AddHours(1));
        job.MarkSnapshotUnresolved("Restore the source");
        job.GetSnapshotState().Should().Be("Unavailable");
        job.SnapshotUnavailableAtUtc.Should().Be(removedAt);
        job.SnapshotRecoveryReason.Should().Contain("Source part removed");
        var capture = () => job.CaptureProductionSnapshot(part);
        capture.Should().Throw<ProductionSnapshotUnavailableException>();
        var reset = () => job.ResetUnsentBackfillSnapshot(part);
        reset.Should().Throw<ProductionSnapshotUnavailableException>();
    }

    [Fact]
    public void SourceRemoval_DoesNotDisableVerifiedSnapshotsAndRejectsUnfinishedLegacyJobs()
    {
        var part = ProductionTestFixtures.CreatePart();
        var legacy = PrintJob.Create("Test part", Guid.NewGuid(), 1024, part.UnitsPerPrint,
            AssignmentMode.Unassigned, null, part.RevisionHash, part.RevisionNumber, ProductionJobRequirements.FromPart(part));
        var remove = () => legacy.MarkSnapshotUnavailable(DateTimeOffset.UtcNow);
        remove.Should().Throw<InvalidOperationException>();
        legacy.SnapshotUnavailableAtUtc.Should().BeNull();
        legacy.CaptureProductionSnapshot(part);
        legacy.MarkCancelled();
        legacy.MarkSnapshotUnavailable(DateTimeOffset.UtcNow);
        legacy.GetSnapshotState().Should().Be("Ready");
        legacy.SnapshotUnavailableAtUtc.Should().BeNull();
    }

}
