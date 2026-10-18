using FluentAssertions;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Domain.Tests.Parts;

public class PartRenameTests
{
    [Fact]
    public void Rename_TrimsAndReplacesName()
    {
        var part = NewPart();

        part.Rename("  Bracket v2  ");

        part.Name.Should().Be("Bracket v2");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_BlankName_Throws(string name)
    {
        var part = NewPart();

        part.Invoking(p => p.Rename(name)).Should().Throw<ArgumentException>();
        part.Name.Should().Be("Part");
    }

    private static Part NewPart() =>
        Part.Create("Part", PartFileFormat.Stl, true, "part.stl", "/tmp/part.stl", 100, 0.4m, null, null,
            EstimateStatus.Unavailable, null, null, 1, 1, new string('a', 64), [new FilamentRequirementSpec("PLA", null, null, null, null)], [], []);
}
