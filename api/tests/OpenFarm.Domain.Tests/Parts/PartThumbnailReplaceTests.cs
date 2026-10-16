using FluentAssertions;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Domain.Tests.Parts;

public class PartThumbnailReplaceTests
{
    [Fact]
    public void ReplaceFile_SamePlate_ReusesInstanceWithNewBytes()
    {
        var part = NewPart([new ThumbnailSpec(0, [1])]);
        var original = part.Thumbnails[0];

        Replace(part, [new ThumbnailSpec(0, [2])]);

        part.Thumbnails.Should().ContainSingle().Which.Should().BeSameAs(original);
        part.Thumbnails[0].Bytes.Should().Equal(2);
    }

    [Fact]
    public void ReplaceFile_FewerPlates_RemovesTheRest()
    {
        var part = NewPart([new ThumbnailSpec(0, [1]), new ThumbnailSpec(1, [1])]);

        Replace(part, [new ThumbnailSpec(0, [2])]);

        part.Thumbnails.Should().ContainSingle(t => t.PlateIndex == 0);
    }

    private static void Replace(Part part, IReadOnlyList<ThumbnailSpec> thumbnails) =>
        part.ReplaceFile(PartFileFormat.GCode, false, "b.gcode", "/tmp/b.gcode", 100, 0.4m, null, null,
            EstimateStatus.Unavailable, null, null, 1, new string('b', 64),
            [new FilamentRequirementSpec("PLA", null, null, null, null)], thumbnails);

    private static Part NewPart(IReadOnlyList<ThumbnailSpec> thumbnails) =>
        Part.Create(Guid.NewGuid(), "Part", PartFileFormat.GCode, false, "a.gcode", "/tmp/a.gcode", 100, 0.4m, null, null,
            EstimateStatus.Unavailable, null, null, 1, 1, new string('a', 64),
            [new FilamentRequirementSpec("PLA", null, null, null, null)], thumbnails, []);
}
