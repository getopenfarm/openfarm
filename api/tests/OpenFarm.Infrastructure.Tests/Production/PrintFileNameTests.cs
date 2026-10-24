using FluentAssertions;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Infrastructure.Tests.Production;

public class PrintFileNameTests
{
    [Fact]
    public void Describe_UsesDistinctMaterialsAndAvailableDuration()
    {
        PrintFileName.Describe("Bracket.stl", ["PLA", "PLA", "PETG"], 80)
            .Should().Be("Bracket_PLA_PETG_1h20m");
        PrintFileName.Describe("Text.gcode.3mf", [null], null).Should().Be("Text");
    }

    [Theory]
    [InlineData("Bracket_PLA_1h20m.gcode.3mf", "Bracket_PLA_1h20m")]
    [InlineData("../part....gcode\r\nDELE other", "part_gcode_DELE_other")]
    [InlineData("雪.stl", "Print")]
    public void ForUpload_RemovesExtensionsAndUnsafeCharacters(string input, string stem)
    {
        PrintFileName.ForUpload(input).Should().MatchRegex($@"^{stem}_\d{{8}}-\d{{9}}\.gcode\.3mf$");
    }

    [Fact]
    public void ForUpload_LimitsLengthAndKeepsConcurrentUploadsDistinct()
    {
        var names = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 100, _ => names.Add(PrintFileName.ForUpload(new string('a', 500))));
        names.Should().OnlyHaveUniqueItems();
        names.Should().OnlyContain(n => n.Length <= 130 && n.EndsWith(".gcode.3mf", StringComparison.Ordinal));
    }
}
