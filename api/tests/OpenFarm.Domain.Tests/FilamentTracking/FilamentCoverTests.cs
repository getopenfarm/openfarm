using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class FilamentCoverTests
{
    private static readonly Guid White = Guid.NewGuid();
    private static readonly Guid Black = Guid.NewGuid();

    [Fact]
    public void Needed_AddsTheSafetyMargin()
    {
        FilamentCover.Needed(120m).Should().Be(132m);
    }

    [Fact]
    public void Available_ExternalSpool_IsOnlyItsOwnWeight()
    {
        FilamentCover.Available(ReloadPolicy.None, White, 50m, [(1, White, 900m)]).Should().Be(50m);
    }

    [Fact]
    public void Available_MultiMaterialUnit_AddsEveryLoadedSpoolOfTheSameColor()
    {
        var available = FilamentCover.Available(ReloadPolicy.MatchRequired, White, 50m,
            [(1, White, 300m), (2, Black, 1000m), (3, White, 200m), (0, White, 0m)]);

        available.Should().Be(550m);
    }

    [Fact]
    public void Available_Reloader_StopsAtTheFirstOtherColorInSlotOrder()
    {
        // It switches to the lowest loaded slot whatever is in it, so a different colour ends what counts.
        var available = FilamentCover.Available(ReloadPolicy.Unconditional, White, 50m,
            [(3, White, 400m), (1, White, 300m), (2, Black, 1000m)]);

        available.Should().Be(350m);
    }

    [Fact]
    public void Rank_EmptiestThatFitsAlone_ThenBackupCovered_ThenFullest()
    {
        var spools = new[]
        {
            (Name: "full", Remaining: 1000m, Available: 1000m),
            (Name: "partial", Remaining: 200m, Available: 200m),
            (Name: "tiny-with-backup", Remaining: 50m, Available: 1050m),
            (Name: "short", Remaining: 60m, Available: 60m),
            (Name: "shorter", Remaining: 40m, Available: 40m),
        };

        var order = spools.OrderBy(s => FilamentCover.Rank(120m, s.Remaining, s.Available)).Select(s => s.Name);

        order.Should().Equal("partial", "full", "tiny-with-backup", "short", "shorter");
    }

    [Fact]
    public void Rank_WithoutAnEstimate_RanksEverySpoolTheSame()
    {
        FilamentCover.Rank(null, 50m, 50m).Should().Be(FilamentCover.Rank(null, 1000m, 1000m));
    }

    [Fact]
    public void FirstShortfall_AddsUpLinesDrawingOnTheSamePool()
    {
        var unit = Guid.NewGuid();

        var shortfall = FilamentCover.FirstShortfall([(unit, White, 250m, 100m), (unit, White, 250m, 130m)]);

        shortfall.Should().Be((1, 230m, 253m, 250m));
    }

    [Fact]
    public void FirstShortfall_SkipsLinesWithoutAnEstimate_AndKeepsPoolsApart()
    {
        var unit = Guid.NewGuid();

        FilamentCover.FirstShortfall([(unit, White, 250m, 200m), (unit, Black, 10m, null), (Guid.NewGuid(), White, 250m, 200m)])
            .Should().BeNull();
    }

    [Fact]
    public void FirstShortfall_AppliesTheMargin()
    {
        FilamentCover.FirstShortfall([(Guid.NewGuid(), White, 130m, 120m)]).Should().Be((0, 120m, 132m, 130m));
    }
}
