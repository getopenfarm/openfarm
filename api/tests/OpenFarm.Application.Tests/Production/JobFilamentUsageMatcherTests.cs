using FluentAssertions;
using OpenFarm.Application.Production;

namespace OpenFarm.Application.Tests.Production;

public class JobFilamentUsageMatcherTests
{
    private static IEnumerable<decimal?> Grams(IReadOnlyList<(string?, string?)> lines, params FilamentUsage[] usages) =>
        JobFilamentUsageMatcher.Match(lines, usages).Select(l => l.Grams);

    [Fact]
    public void Match_AssignsEachUsageToItsOwnLine_ForMultiColour()
    {
        var lines = new (string?, string?)[] { ("PLA", "#FF0000"), ("PLA", "#FFFFFF") };

        var result = JobFilamentUsageMatcher.Match(lines,
            [new FilamentUsage("PLA", "#ffffff", 4.5m, 1), new FilamentUsage("PLA", "#FF0000", 12m, 0)]);

        result.Select(l => l.Grams).Should().Equal(12m, 4.5m);
        result.Select(l => l.Tools).Should().BeEquivalentTo(new[] { new[] { 0 }, new[] { 1 } }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void Match_SumsUsagesThatShareALine_AndKeepsEveryTool()
    {
        var lines = new (string?, string?)[] { ("PETG", "#000000") };

        var result = JobFilamentUsageMatcher.Match(lines,
            [new FilamentUsage("PETG", "#000000", 3m, 0), new FilamentUsage("PETG", "#000000", 2m, 2)]);

        result.Single().Grams.Should().Be(5m);
        result.Single().Tools.Should().Equal(0, 2);
    }

    [Fact]
    public void Match_SingleLineWithOverriddenColour_GetsWholeFileUsage()
    {
        var result = JobFilamentUsageMatcher.Match([("PLA", "#123456")], [new FilamentUsage("PLA", "#FF0000", 7m, 3)]);

        result.Single().Grams.Should().Be(7m);
        result.Single().Tools.Should().Equal(3);
    }

    [Fact]
    public void Match_UnmatchedLineAmongSeveral_IsNull() =>
        Grams([("PLA", "#FF0000"), ("PLA", "#00FF00")], new FilamentUsage("PLA", "#FF0000", 7m)).Should().Equal(7m, null);

    [Fact]
    public void Match_NoUsages_IsAllNull() =>
        JobFilamentUsageMatcher.Match([("PLA", null)], []).Should().ContainSingle(l => l.Grams == null && l.Tools.Count == 0);
}
