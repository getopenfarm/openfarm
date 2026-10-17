using FluentAssertions;
using OpenFarm.Application.Production;

namespace OpenFarm.Application.Tests.Production;

public class PrintFeedMappingBuilderTests
{
    private static MappedLine Ams(int unit, int slot, params int[] tools) => new(tools, new FeedPlacement(FeedPath.Ams, unit, slot));

    private static MappedLine External(params int[] tools) => new(tools, new FeedPlacement(FeedPath.External, 0, 0));

    [Fact]
    public void SingleColourInAnAmsSlot_UsesAms()
    {
        var mapping = PrintFeedMappingBuilder.Build([Ams(0, 2, 0)], filamentCount: 1)!;

        mapping.UseAms.Should().BeTrue();
        mapping.Trays.Should().Equal(2);
        mapping.Targets.Select(t => (t.AmsId, t.SlotId)).Should().Equal((0, 2));
    }

    [Fact]
    public void TwoColours_MapEachFileFilamentToItsOwnTray()
    {
        // The real two-colour test file: tool 0 = black, tool 1 = white; black sits in slot 3, white in slot 1.
        var mapping = PrintFeedMappingBuilder.Build([Ams(0, 2, 0), Ams(0, 0, 1)], filamentCount: 2)!;

        mapping.Trays.Should().Equal(2, 0);
    }

    [Fact]
    public void UnusedProjectFilaments_AreUnmapped_AndTheArrayCoversEveryProjectFilament()
    {
        var mapping = PrintFeedMappingBuilder.Build([Ams(0, 1, 2)], filamentCount: 4)!;

        mapping.Trays.Should().Equal(-1, -1, 1, -1);
        mapping.Targets.Select(t => (t.AmsId, t.SlotId)).Should().Equal((255, 255), (255, 255), (0, 1), (255, 255));
    }

    [Fact]
    public void TheArrayIsAtLeastAsLongAsTheHighestToolUsed() =>
        PrintFeedMappingBuilder.Build([Ams(0, 0, 3)], filamentCount: 0)!.Trays.Should().Equal(-1, -1, -1, 0);

    [Theory]
    [InlineData(0, 3, 3)]
    [InlineData(1, 0, 4)]
    [InlineData(1, 3, 7)]
    [InlineData(3, 3, 15)]
    public void FourSlotUnits_UseUnitTimesFourPlusSlot(int unit, int slot, int tray) =>
        PrintFeedMappingBuilder.Build([Ams(unit, slot, 0)], 1)!.Trays.Should().Equal(tray);

    [Fact]
    public void SingleSlotUnit_UsesItsUnitIdAndSlotZero()
    {
        var mapping = PrintFeedMappingBuilder.Build([Ams(128, 0, 0)], 1)!;

        mapping.Trays.Should().Equal(128);
        mapping.Targets.Single().Should().Be(new OpenFarm.Application.Printers.FeedTarget(128, 0));
    }

    [Fact]
    public void ExternalOnly_DoesNotUseAms_AndNamesTheExternalFeedOnlyInTheSecondArray()
    {
        var mapping = PrintFeedMappingBuilder.Build([External(0)], 1)!;

        mapping.UseAms.Should().BeFalse();
        mapping.Trays.Should().Equal(-1);
        mapping.Targets.Single().Should().Be(new OpenFarm.Application.Printers.FeedTarget(255, 0));
    }

    [Fact]
    public void AmsMixedWithExternal_IsRefused()
    {
        PrintFeedMappingBuilder.Build([Ams(0, 0, 0), External(1)], 2).Should().BeNull();
        PrintFeedMappingBuilder.MixesAmsWithExternal([new FeedPlacement(FeedPath.Ams, 0, 0), new FeedPlacement(FeedPath.External, 0, 0)]).Should().BeTrue();
        PrintFeedMappingBuilder.MixesAmsWithExternal([new FeedPlacement(FeedPath.Ams, 0, 0), new FeedPlacement(FeedPath.Ams, 0, 1)]).Should().BeFalse();
        PrintFeedMappingBuilder.MixesAmsWithExternal([]).Should().BeFalse();
    }

    [Fact]
    public void SingleLineWithNoKnownTool_IsReadAsTheFirstFilament() =>
        PrintFeedMappingBuilder.Build([Ams(0, 2)], 1)!.Trays.Should().Equal(2);

    [Fact]
    public void SeveralLinesWithNoKnownTools_CannotBeMapped()
    {
        PrintFeedMappingBuilder.Build([Ams(0, 0), Ams(0, 1)], 2).Should().BeNull();
        PrintFeedMappingBuilder.Build([], 0).Should().BeNull();
    }

    [Fact]
    public void AColourSplitOverSeveralFileFilaments_FeedsAllOfThemFromTheSameTray() =>
        PrintFeedMappingBuilder.Build([Ams(0, 1, 0, 2)], 3)!.Trays.Should().Equal(1, -1, 1);
}
