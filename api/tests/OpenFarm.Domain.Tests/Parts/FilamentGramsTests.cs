using FluentAssertions;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Domain.Tests.Parts;

public class FilamentGramsTests
{
    private static readonly (string?, string?)[] BlackWhite = [("PLA", "#000000"), ("PLA", "#FFFFFF")];

    [Fact]
    public void MatchesEachRequirementByMaterialAndColor()
    {
        var split = FilamentGrams.Distribute(BlackWhite,
            [new("PLA", "#FFFFFF", 4.5m), new("pla", "#000000", 12m)]);

        split.Should().Equal(12m, 4.5m);
    }

    [Fact]
    public void SumsToolsThatShareAColor()
    {
        var split = FilamentGrams.Distribute(BlackWhite,
            [new("PLA", "#000000", 5m), new("PLA", "#000000", 7m), new("PLA", "#FFFFFF", 1m)]);

        split.Should().Equal(12m, 1m);
    }

    [Fact]
    public void LoneRequirementTakesEverything()
    {
        // The STL "any material" wildcard has no colour to match against.
        var split = FilamentGrams.Distribute([(null, null)], [new("PLA", "#123456", 3m), new("PLA", "#654321", 2m)]);

        split.Should().Equal(5m);
    }

    [Fact]
    public void UnmatchedGrams_LeaveEveryRequirementUnknown()
    {
        // A partial split would sum to less than the file's total, so none is better than a wrong one.
        var split = FilamentGrams.Distribute(BlackWhite, [new("PLA", "#000000", 12m), new("PLA", "#FF0000", 2m)]);

        split.Should().Equal(new decimal?[] { null, null });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoGrams_LeaveEveryRequirementUnknown(bool empty)
    {
        FilamentGrams.Distribute(BlackWhite, empty ? [] : null).Should().Equal(new decimal?[] { null, null });
    }
}
