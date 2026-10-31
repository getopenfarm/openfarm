using FluentAssertions;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

// A finite requirement is done when every lost plate (failed or cancelled) was either reprinted or written off, and the
// completed plus written-off units reach the target. An unlimited requirement never holds a run open.
public class RunCompletionRuleTests
{
    private static readonly Guid RequirementId = Guid.NewGuid();

    private static PrintJob Job(int units = 4) => PrintJob.Create(
        "bracket", RequirementId, 1024, units, AssignmentMode.Unassigned, null, "hash", 1, [new JobFilamentRequirementSpec("PLA", null)]);

    private static PrintJob Completed(int units = 4)
    {
        var job = Job(units);
        job.MarkStarted();
        job.MarkCompleted();
        return job;
    }

    private static PrintJob Failed(int units = 4)
    {
        var job = Job(units);
        job.MarkStarted();
        job.MarkFailed();
        return job;
    }

    private static PrintJob Cancelled()
    {
        var job = Job();
        job.MarkCancelled();
        return job;
    }

    private static PartRequirement Requirement(decimal? quantity) => PartRequirement.Create(Guid.NewGuid(), Guid.NewGuid(), quantity);

    [Fact]
    public void Unlimited_IsAlwaysSatisfied() =>
        Requirement(null).IsSatisfiedBy([Job()]).Should().BeTrue();

    [Fact]
    public void CompletedUnitsReachingTheTarget_Satisfy() =>
        Requirement(8).IsSatisfiedBy([Completed(), Completed()]).Should().BeTrue();

    [Fact]
    public void CompletedUnitsShortOfTheTarget_DoNotSatisfy() =>
        Requirement(8).IsSatisfiedBy([Completed(), Job()]).Should().BeFalse();

    [Fact]
    public void AnUnresolvedFailedPlate_BlocksEvenWhenOtherPlatesCoverTheTarget() =>
        Requirement(4).IsSatisfiedBy([Completed(), Failed()]).Should().BeFalse();

    [Fact]
    public void AFailedPlateReprintedByItsReplacement_Satisfies()
    {
        var failed = Failed();
        var replacement = Completed();
        failed.MarkRequeuedAs(replacement.Id);

        Requirement(8).IsSatisfiedBy([Completed(), failed, replacement]).Should().BeTrue();
    }

    [Fact]
    public void AWrittenOffPlate_CountsTowardTheTarget()
    {
        var failed = Failed();
        failed.WriteOff();

        Requirement(8).IsSatisfiedBy([Completed(), failed]).Should().BeTrue();
    }

    [Fact]
    public void AWrittenOffCancelledPlate_CountsTowardTheTarget()
    {
        var cancelled = Cancelled();
        cancelled.WriteOff();

        Requirement(8).IsSatisfiedBy([Completed(), cancelled]).Should().BeTrue();
    }

    [Fact]
    public void WriteOff_OnlyAfterThePlateIsLost()
    {
        var queued = Job();
        var completed = Completed();

        queued.Invoking(j => j.WriteOff()).Should().Throw<PrintJobNotEligibleForWriteOffException>();
        completed.Invoking(j => j.WriteOff()).Should().Throw<PrintJobNotEligibleForWriteOffException>();
    }

    [Fact]
    public void WriteOff_And_Requeue_ExcludeEachOther()
    {
        var requeued = Failed();
        requeued.MarkRequeuedAs(Guid.NewGuid());
        var writtenOff = Failed();
        writtenOff.WriteOff();

        requeued.Invoking(j => j.WriteOff()).Should().Throw<PrintJobNotEligibleForWriteOffException>();
        writtenOff.Invoking(j => j.WriteOff()).Should().Throw<PrintJobNotEligibleForWriteOffException>();
        writtenOff.Invoking(j => j.MarkRequeuedAs(Guid.NewGuid())).Should().Throw<PrintJobNotEligibleForRequeueException>();
    }

    [Fact]
    public void Close_FromActive_OnlyWhenNoFiniteWorkRemains()
    {
        var run = ProductionRun.Create(null, null, null,
            [new ProductionDemandSpec(RunLineTargetKind.Part, Guid.NewGuid(), null, null, AssignmentMode.Unassigned, null, 1, null)]);

        run.Invoking(r => r.Close(finiteWorkRemains: true)).Should().Throw<InvalidRunCompletionException>();
        run.Close(finiteWorkRemains: false);

        run.Status.Should().Be(ProductionRunStatus.Closed);
    }

    [Fact]
    public void AddLines_ReopensACompleteRun_AndIsRefusedOnceClosed()
    {
        var spec = new ProductionDemandSpec(RunLineTargetKind.Part, Guid.NewGuid(), 4, null, AssignmentMode.Unassigned, null, 1, null);
        var run = ProductionRun.Create(null, null, null, [spec]);
        run.MarkComplete();

        var added = run.AddLines([spec]);

        added.Should().ContainSingle();
        run.Lines.Should().HaveCount(2);
        run.Status.Should().Be(ProductionRunStatus.Active);

        run.MarkComplete();
        run.Close(finiteWorkRemains: false);
        run.Invoking(r => r.AddLines([spec])).Should().Throw<ProductionRunNotOpenException>();
    }

    [Fact]
    public void UpdateDetails_TrimsBlanksToNull_AndIsRefusedOnceCancelled()
    {
        var run = ProductionRun.Create("Old", null, null,
            [new ProductionDemandSpec(RunLineTargetKind.Part, Guid.NewGuid(), 4, null, AssignmentMode.Unassigned, null, 1, null)]);

        run.UpdateDetails("  Order #1042 ", new DateOnly(2026, 10, 31), "  ");

        run.Name.Should().Be("Order #1042");
        run.DueDate.Should().Be(new DateOnly(2026, 10, 31));
        run.Notes.Should().BeNull();

        run.Cancel();
        run.Invoking(r => r.UpdateDetails("x", null, null)).Should().Throw<ProductionRunNotOpenException>();
    }
}
