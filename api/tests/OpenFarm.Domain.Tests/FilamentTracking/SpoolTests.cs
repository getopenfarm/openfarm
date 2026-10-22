using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class SpoolTests
{
    private static Spool NewSpool(decimal spoolWeightGrams = 1000, decimal? remainingWeightGrams = null) =>
        Spool.Create(Guid.NewGuid(), "Shelf A-1", spoolWeightGrams, remainingWeightGrams ?? spoolWeightGrams);

    [Fact]
    public void Create_DefaultsRemainingWeightToSpoolWeight_AndStartsInStorage()
    {
        var spool = Spool.Create(Guid.NewGuid(), null, 1000, 1000);

        spool.RemainingWeightGrams.Should().Be(1000);
        spool.State.Should().Be(SpoolState.InStorage);
        spool.HasUsageOverrun.Should().BeFalse();
    }

    [Fact]
    public void RecordUsage_DecrementsRemainingWeight()
    {
        var spool = NewSpool(1000);
        spool.LoadOntoFeedSlot(Guid.NewGuid());

        spool.RecordUsage(300);

        spool.RemainingWeightGrams.Should().Be(700);
        spool.HasUsageOverrun.Should().BeFalse();
    }

    [Fact]
    public void RecordUsage_CumulativeUsageExceedingRemaining_FloorsAtZeroAndFlagsOverrun()
    {
        var spool = NewSpool(1000);
        spool.LoadOntoFeedSlot(Guid.NewGuid());

        spool.RecordUsage(700);
        spool.RecordUsage(500);

        spool.RemainingWeightGrams.Should().Be(0);
        spool.HasUsageOverrun.Should().BeTrue();
    }

    [Fact]
    public void RecordUsage_WhenNotUsing_Throws()
    {
        var spool = NewSpool(1000);

        var act = () => spool.RecordUsage(100);

        act.Should().Throw<InvalidSpoolStateTransitionException>();
    }

    [Fact]
    public void LoadOntoFeedSlot_TransitionsToUsingAndSetsFeedSlotId()
    {
        var spool = NewSpool();
        var feedSlotId = Guid.NewGuid();

        spool.LoadOntoFeedSlot(feedSlotId);

        spool.State.Should().Be(SpoolState.Using);
        spool.FeedSlotId.Should().Be(feedSlotId);
    }

    [Fact]
    public void ReturnToStorage_RetainsRemainingWeightAndClearsFeedSlot()
    {
        var spool = NewSpool(1000);
        spool.LoadOntoFeedSlot(Guid.NewGuid());
        spool.RecordUsage(400);

        spool.ReturnToStorage();

        spool.State.Should().Be(SpoolState.InStorage);
        spool.FeedSlotId.Should().BeNull();
        spool.RemainingWeightGrams.Should().Be(600);
    }

    [Fact]
    public void MarkFinished_TransitionsToFinishedAndClearsFeedSlot()
    {
        var spool = NewSpool();
        spool.LoadOntoFeedSlot(Guid.NewGuid());

        spool.MarkFinished();

        spool.State.Should().Be(SpoolState.Finished);
        spool.FeedSlotId.Should().BeNull();
    }

    [Fact]
    public void MarkFinished_FromStorage_WritesOffRemainingAndZeroesWeight()
    {
        var spool = NewSpool(1000, 340);

        var writeOff = spool.MarkFinished("Damaged");

        spool.State.Should().Be(SpoolState.Finished);
        spool.RemainingWeightGrams.Should().Be(0);
        writeOff.Should().NotBeNull();
        writeOff!.SpoolId.Should().Be(spool.Id);
        writeOff.Source.Should().Be(SpoolUsageSource.WriteOff);
        writeOff.GramsConsumed.Should().Be(340);
        writeOff.RemainingAfterGrams.Should().Be(0);
        writeOff.Reason.Should().Be("Damaged");
    }

    [Fact]
    public void MarkFinished_WithNothingLeft_ReturnsNoWriteOff()
    {
        var spool = NewSpool(1000);
        spool.LoadOntoFeedSlot(Guid.NewGuid());
        spool.RecordUsage(1000);

        var writeOff = spool.MarkFinished();

        writeOff.Should().BeNull();
        spool.State.Should().Be(SpoolState.Finished);
    }

    [Fact]
    public void MarkFinished_WhenFinished_Throws()
    {
        var spool = NewSpool();
        spool.MarkFinished();

        var act = () => spool.MarkFinished();

        act.Should().Throw<InvalidSpoolStateTransitionException>();
    }

    [Fact]
    public void ReturnToStorage_WhenFinished_Throws()
    {
        var spool = NewSpool();
        spool.MarkFinished();

        var act = () => spool.ReturnToStorage();

        act.Should().Throw<InvalidSpoolStateTransitionException>();
    }

    [Fact]
    public void Restore_PutsBackLatestWriteOff()
    {
        var spool = NewSpool(1000, 340);
        var writeOff = spool.MarkFinished();

        var restore = spool.Restore(writeOff);

        spool.State.Should().Be(SpoolState.InStorage);
        spool.FeedSlotId.Should().BeNull();
        spool.RemainingWeightGrams.Should().Be(340);
        restore.Should().NotBeNull();
        restore!.Source.Should().Be(SpoolUsageSource.Restore);
        restore.GramsConsumed.Should().Be(-340);
        restore.RemainingAfterGrams.Should().Be(340);
    }

    [Fact]
    public void Restore_AfterReweigh_KeepsCorrectedWeight()
    {
        var spool = NewSpool(1000, 340);
        spool.MarkFinished();
        spool.CorrectRemainingWeight(120);
        var reweigh = SpoolUsageRecord.Correction(spool.Id, 0, 120, "weighed on scale");

        var restore = spool.Restore(reweigh);

        restore.Should().BeNull();
        spool.State.Should().Be(SpoolState.InStorage);
        spool.RemainingWeightGrams.Should().Be(120);
    }

    [Fact]
    public void Restore_WithAnotherSpoolsWriteOff_PutsNothingBack()
    {
        var spool = NewSpool(1000, 340);
        spool.MarkFinished();
        var otherWriteOff = NewSpool(1000, 500).MarkFinished();

        var restore = spool.Restore(otherWriteOff);

        restore.Should().BeNull();
        spool.RemainingWeightGrams.Should().Be(0);
    }

    [Fact]
    public void Restore_WithNoHistory_KeepsWeight()
    {
        var spool = NewSpool(1000);
        spool.LoadOntoFeedSlot(Guid.NewGuid());
        spool.RecordUsage(1000);
        spool.MarkFinished();

        var restore = spool.Restore(null);

        restore.Should().BeNull();
        spool.State.Should().Be(SpoolState.InStorage);
        spool.RemainingWeightGrams.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Restore_WhenNotFinished_Throws(bool loaded)
    {
        var spool = NewSpool();
        if (loaded) spool.LoadOntoFeedSlot(Guid.NewGuid());

        var act = () => spool.Restore(null);

        act.Should().Throw<InvalidSpoolStateTransitionException>();
    }

    [Fact]
    public void FinishThenRestore_LedgerBalances()
    {
        var spool = NewSpool(1000, 340);
        var start = spool.RemainingWeightGrams;

        var writeOff = spool.MarkFinished("Used up")!;
        var restore = spool.Restore(writeOff)!;

        (start - writeOff.GramsConsumed - restore.GramsConsumed).Should().Be(spool.RemainingWeightGrams);
    }

    [Fact]
    public void CorrectRemainingWeight_OverwritesEstimateAndClearsOverrun()
    {
        var spool = NewSpool(1000);
        spool.LoadOntoFeedSlot(Guid.NewGuid());
        spool.RecordUsage(1100);

        spool.HasUsageOverrun.Should().BeTrue();

        spool.CorrectRemainingWeight(150);

        spool.RemainingWeightGrams.Should().Be(150);
        spool.HasUsageOverrun.Should().BeFalse();
    }

    [Fact]
    public void CorrectRemainingWeight_AboveSpoolWeight_Throws()
    {
        var spool = NewSpool(1000);

        var act = () => spool.CorrectRemainingWeight(1001);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CorrectRemainingWeight_Negative_Throws()
    {
        var spool = NewSpool(1000);

        var act = () => spool.CorrectRemainingWeight(-1);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Create_WithNonPositiveSpoolWeight_Throws(decimal spoolWeightGrams)
    {
        var act = () => Spool.Create(Guid.NewGuid(), null, spoolWeightGrams, spoolWeightGrams <= 0 ? 1 : spoolWeightGrams);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithRemainingWeightAboveSpoolWeight_Throws()
    {
        var act = () => Spool.Create(Guid.NewGuid(), null, 500, 600);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Relabel_TrimsLabel()
    {
        var spool = NewSpool();

        spool.Relabel("  Shelf B-2 ");

        spool.Label.Should().Be("Shelf B-2");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Relabel_BlankLabel_ClearsIt(string? label)
    {
        var spool = NewSpool();

        spool.Relabel(label);

        spool.Label.Should().BeNull();
    }
}
