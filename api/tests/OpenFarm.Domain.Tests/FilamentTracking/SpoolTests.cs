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
