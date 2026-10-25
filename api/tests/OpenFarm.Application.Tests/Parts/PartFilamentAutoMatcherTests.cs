using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Tests.Parts;

// TDD — small, deterministic selection rule on top of ToolTrayMatcher.
public class PartFilamentAutoMatcherTests
{
    private static readonly Guid Black = Guid.NewGuid();
    private static readonly Guid White = Guid.NewGuid();

    private static PreviewFilamentRequirement Requirement(string? material = "PLA", string? color = "#000000", string? vendor = "Snapmaker",
        Guid? narrow = null) => new(0, material, color, vendor, narrow);

    private static CatalogColour Colour(Guid id, string vendor, string material, string hex) => new(id, vendor, material, hex);

    private static Guid? Match(PreviewFilamentRequirement requirement, params CatalogColour[] catalog) =>
        PartFilamentAutoMatcher.Apply([requirement], catalog)[0].NarrowFilamentVariantId;

    [Fact]
    public void Apply_OneColourOfSameVendorAndMaterial_SelectsIt()
    {
        Match(Requirement(), Colour(Black, "snapmaker", "PLA", "#050505"), Colour(White, "Snapmaker", "PLA", "#FFFFFF"))
            .Should().Be(Black);
    }

    [Fact]
    public void Apply_TwoEquallyCloseColours_SelectsNothing()
    {
        Match(Requirement(), Colour(Black, "Snapmaker", "PLA", "#000000"), Colour(White, "Snapmaker", "PLA", "#010101"))
            .Should().BeNull();
    }

    [Fact]
    public void Apply_OtherVendor_SelectsNothing() =>
        Match(Requirement(), Colour(Black, "Aliz", "PLA", "#000000")).Should().BeNull();

    [Fact]
    public void Apply_OtherMaterial_SelectsNothing() =>
        Match(Requirement(), Colour(Black, "Snapmaker", "PETG", "#000000")).Should().BeNull();

    [Fact]
    public void Apply_NoVendorInFile_MatchesOnMaterialAndColour() =>
        Match(Requirement(vendor: null), Colour(Black, "Aliz", "PLA", "#000000")).Should().Be(Black);

    [Fact]
    public void Apply_AlreadyNarrowed_KeepsTheSelection() =>
        Match(Requirement(narrow: White), Colour(Black, "Snapmaker", "PLA", "#000000")).Should().Be(White);

    [Fact]
    public void Apply_NoColourInFile_SelectsNothing() =>
        Match(Requirement(color: null), Colour(Black, "Snapmaker", "PLA", "#000000")).Should().BeNull();
}
