using FluentAssertions;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

public class ObjectSkippingTests
{
    private static PrintObjectManifest Manifest() => new("hash", 1, "Bambu",
        [new("1", "left"), new("2", "right"), new("3", "left copy"), new("4", "right copy")],
        [new("1", ["1", "2"]), new("2", ["3", "4"])]);
    private static PrintJob Job() => PrintJob.Create("kit", Guid.NewGuid(), 1024, 2,
        AssignmentMode.Unassigned, null, "hash", 1, [new("PLA", null)]);

    [Fact]
    public void MultipartRemainder_ExcludesWholeUnit_AndNeedsPrinterConfirmation()
    {
        var job = Job();
        job.SetExactTarget(1, Manifest());
        job.ObjectExecution!.PlannedExclusions.Should().BeEquivalentTo("3", "4");
        job.MarkStarted();
        job.MarkCompleted();
        job.ProducedUnits.Should().Be(0);
        job.ObserveExcludedObjects(["3", "4"]);
        job.ProducedUnits.Should().Be(1);
        job.MissingUnits.Should().Be(0);
    }

    [Fact]
    public void OneSkippedMember_LosesOneCompleteUnit_AndWriteOffOnlyResolvesTheLoss()
    {
        var job = Job();
        job.SetExactTarget(2, Manifest());
        job.MarkStarted();
        job.ObserveExcludedObjects(["2"]);
        job.MarkCompleted();
        job.ProducedUnits.Should().Be(1);
        job.IsUnresolvedLoss.Should().BeTrue();
        job.WriteOff();
        job.WrittenOffUnits.Should().Be(1);
        PartRequirement.Create(Guid.NewGuid(), Guid.NewGuid(), 2).IsSatisfiedBy([job]).Should().BeTrue();
    }

    [Fact]
    public void PlannedSurplus_IsNeverAReplacementShortfall()
    {
        var job = Job();
        job.SetExactTarget(1, Manifest());
        job.MarkStarted();
        job.ObserveExcludedObjects(["3", "4"]);
        job.MarkCompleted();
        job.IsUnresolvedLoss.Should().BeFalse();
        job.PlateQuantity.Should().Be(2);
        job.EffectiveTargetUnits.Should().Be(1);
    }

    [Fact]
    public void DuplicateOrMissingGroupMembers_AreRejected()
    {
        var invalid = Manifest() with { Units = [new("1", ["1", "2"]), new("2", ["2", "4"])] };
        var act = () => invalid.Validate(2);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void LegacyCompletedJobs_KeepTheirHistoricalWholePlateOutput()
    {
        var job = Job();
        job.MarkStarted();
        job.MarkCompleted();
        job.ProducedUnits.Should().Be(2);
    }
}
