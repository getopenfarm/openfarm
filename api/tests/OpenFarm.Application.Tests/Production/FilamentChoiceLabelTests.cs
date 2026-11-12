using FluentAssertions;
using OpenFarm.Application.Production;

namespace OpenFarm.Application.Tests.Production;

public class FilamentChoiceLabelTests
{
    [Theory]
    [InlineData("PETG", "000000", false, "Any PETG · #000000")]
    [InlineData("PETG", "#ffffff", false, "Any PETG · #FFFFFF")]
    [InlineData("PETG", "#ffffff", true, "Any PETG · Any color")]
    [InlineData("PETG", null, false, "Any PETG · Any color")]
    [InlineData(null, null, false, "Any material · Any color")]
    public void Generic_NamesTheMaterialScopeThenTheColour(string? material, string? hex, bool anyColor, string expected) =>
        FilamentChoiceLabel.Generic(material, hex, anyColor).Should().Be(expected);

    [Fact]
    public void Catalog_NamesVendorAndMaterialThenTheColourOrAnyColor()
    {
        FilamentChoiceLabel.Catalog("Aliz", "PETG Basic", "Black").Should().Be("Aliz PETG Basic · Black");
        FilamentChoiceLabel.Catalog("Aliz", "PETG Basic").Should().Be("Aliz PETG Basic · Any color");
        FilamentChoiceLabel.Catalog(null, "PETG Basic").Should().Be("PETG Basic · Any color");
    }
}
