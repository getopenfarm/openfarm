using FluentAssertions;
using OpenFarm.Application.Production;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Tests.Production;

public class PrintFeedMappingBuilderTests
{
    private static FeedSlotRef AmsSlot(int unit, int slot) => new(FeedUnitKindCatalog.MultiMaterialUnit, unit, slot);

    private static readonly FeedSlotRef ExternalPort = new(FeedUnitKindCatalog.ExternalSpool, 0, 0);

    private static MappedLine Ams(int unit, int slot, params int[] tools) => new(tools, AmsSlot(unit, slot));

    private static MappedLine External(params int[] tools) => new(tools, ExternalPort);

    [Fact]
    public void SingleColourInAnAmsSlot_FeedsItsFilamentFromThatSlot() =>
        PrintFeedMappingBuilder.Build([Ams(0, 2, 0)], filamentCount: 1)!.Tools.Should().Equal(AmsSlot(0, 2));

    [Fact]
    public void TwoColours_MapEachFileFilamentToItsOwnSlot()
    {
        // The real two-colour test file: tool 0 = black, tool 1 = white; black sits in slot 3, white in slot 1.
        var mapping = PrintFeedMappingBuilder.Build([Ams(0, 2, 0), Ams(0, 0, 1)], filamentCount: 2)!;

        mapping.Tools.Should().Equal(AmsSlot(0, 2), AmsSlot(0, 0));
    }

    [Fact]
    public void UnusedProjectFilaments_AreUnmapped_AndTheListCoversEveryProjectFilament() =>
        PrintFeedMappingBuilder.Build([Ams(0, 1, 2)], filamentCount: 4)!.Tools.Should().Equal(null, null, AmsSlot(0, 1), null);

    [Fact]
    public void TheListIsAtLeastAsLongAsTheHighestToolUsed() =>
        PrintFeedMappingBuilder.Build([Ams(0, 0, 3)], filamentCount: 0)!.Tools.Should().Equal(null, null, null, AmsSlot(0, 0));

    [Fact]
    public void ExternalOnly_FeedsFromTheExternalPort() =>
        PrintFeedMappingBuilder.Build([External(0)], 1)!.Tools.Should().Equal(ExternalPort);

    [Fact]
    public void AmsMixedWithExternal_IsRefused()
    {
        PrintFeedMappingBuilder.Build([Ams(0, 0, 0), External(1)], 2).Should().BeNull();
        PrintFeedMappingBuilder.MixesAmsWithExternal([AmsSlot(0, 0), ExternalPort]).Should().BeTrue();
        PrintFeedMappingBuilder.MixesAmsWithExternal([AmsSlot(0, 0), AmsSlot(0, 1)]).Should().BeFalse();
        PrintFeedMappingBuilder.MixesAmsWithExternal([]).Should().BeFalse();
    }

    [Fact]
    public void SingleLineWithNoKnownTool_IsReadAsTheFirstFilament() =>
        PrintFeedMappingBuilder.Build([Ams(0, 2)], 1)!.Tools.Should().Equal(AmsSlot(0, 2));

    [Fact]
    public void SeveralLinesWithNoKnownTools_CannotBeMapped()
    {
        PrintFeedMappingBuilder.Build([Ams(0, 0), Ams(0, 1)], 2).Should().BeNull();
        PrintFeedMappingBuilder.Build([], 0).Should().BeNull();
    }

    [Fact]
    public void AColourSplitOverSeveralFileFilaments_FeedsAllOfThemFromTheSameSlot() =>
        PrintFeedMappingBuilder.Build([Ams(0, 1, 0, 2)], 3)!.Tools.Should().Equal(AmsSlot(0, 1), null, AmsSlot(0, 1));
}
