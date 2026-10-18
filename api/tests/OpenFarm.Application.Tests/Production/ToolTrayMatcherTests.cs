using FluentAssertions;
using OpenFarm.Application.Production;

namespace OpenFarm.Application.Tests.Production;

public class ToolTrayMatcherTests
{
    // The AMS of the print that skipped deduction: red, white, black PETG and a tan PLA, colours as the printer reports them (RRGGBBAA).
    private static readonly LoadedTray[] Ams =
    [
        new(0, "PETG", "F72323FF"),
        new(1, "PETG", "FFFFFFFF"),
        new(2, "PETG", "000000FF"),
        new(3, "PLA", "C39054FF"),
    ];

    private static IReadOnlyDictionary<int, int> Match(
        (int, string?, string?)[] tools, IReadOnlyList<LoadedTray>? trays = null, params int[] observed) =>
        ToolTrayMatcher.Match(tools, trays ?? Ams, observed);

    [Fact]
    public void MatchesToolsToTraysOutOfOrder_AndToleratesTheSlicerAndAmsColoursDrifting()
    {
        var match = Match([(0, "PETG", "#000000"), (1, "PETG", "#FB001C")]);

        match.Should().BeEquivalentTo(new Dictionary<int, int> { [0] = 2, [1] = 0 });
    }

    [Fact]
    public void NeverMatchesAnotherMaterial()
    {
        Match([(0, "PETG", "#C39054")]).Should().BeEmpty();
    }

    [Fact]
    public void IgnoresATrayThatIsNotCloseInColour()
    {
        Match([(0, "PETG", "#00FF00")]).Should().BeEmpty();
    }

    [Fact]
    public void LeavesAToolUnmatched_WhenTwoTraysAreEquallyGood_AndNoneWasSeen()
    {
        LoadedTray[] twins = [new(0, "PLA", "FF0000FF"), new(1, "PLA", "FF0000FF")];

        Match([(0, "PLA", "#FF0000")], twins).Should().BeEmpty();
    }

    [Fact]
    public void BreaksATieWithAnObservedTray()
    {
        LoadedTray[] twins = [new(0, "PLA", "FF0000FF"), new(1, "PLA", "FF0000FF")];

        Match([(0, "PLA", "#FF0000")], twins, 1).Should().BeEquivalentTo(new Dictionary<int, int> { [0] = 1 });
    }

    [Fact]
    public void LetsTwoToolsShareATray()
    {
        Match([(0, "PETG", "#000000"), (1, "PETG", "#000000")]).Should().BeEquivalentTo(new Dictionary<int, int> { [0] = 2, [1] = 2 });
    }

    [Fact]
    public void SkipsAToolWithoutAColourOrMaterial()
    {
        Match([(0, "PETG", null), (1, null, "#000000")]).Should().BeEmpty();
    }
}
