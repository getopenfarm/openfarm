using FluentAssertions;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Tests.FilamentTracking;

public class RfidVariantMatchTests
{
    private static VariantStock Color(string material, string colorName, string hex) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Bambu Lab", material, "PLA", colorName, hex, FilamentStock.Of([]), null);

    private static FeedSlotReading Tray(string? subBrand, string colorHex) =>
        new(0, false, "PLA", colorHex, subBrand, 87, false, "5F3D8925633343C29FB9A97FA9125971", 1000);

    [Fact]
    public void Find_TheSubBrandAndTypeWithTheClearlyNearestColor_IsTheMatch()
    {
        var black = Color("PLA Matte", "Charcoal", "#000000");
        var colors = new[] { Color("PLA Matte", "Ivory", "#FFFFF0"), black, Color("PLA Basic", "Black", "#000000") };

        RfidVariantMatch.Find(colors, Tray("PLA Matte", "080808FF")).Should().Be(black);
    }

    [Fact]
    public void Find_TwoEquallyNearColors_IsNoMatch()
    {
        var colors = new[] { Color("PLA Matte", "Charcoal", "#000000"), Color("PLA Matte", "Black", "#050505") };

        RfidVariantMatch.Find(colors, Tray("PLA Matte", "020202FF")).Should().BeNull();
    }

    [Fact]
    public void Find_AnotherSubBrand_IsNoMatch()
    {
        RfidVariantMatch.Find([Color("PLA Basic", "Black", "#000000")], Tray("PLA Matte", "000000FF")).Should().BeNull();
    }

    [Fact]
    public void Find_APlainerName_IsTheMatch_WhenNoFilamentIsNamedForTheLine()
    {
        var plain = Color("PLA", "Red", "#E53935");

        RfidVariantMatch.Find([plain, Color("PLA Matte", "Red", "#E53935")], Tray("PLA Basic", "E53935FF")).Should().Be(plain);
    }

    [Fact]
    public void Find_AFilamentNamedForTheLine_InAnyWordOrder_WinsOverAPlainerName()
    {
        var named = Color("Basic PLA", "Red", "#E53935");

        RfidVariantMatch.Find([Color("PLA", "Red", "#E53935"), named], Tray("PLA Basic", "E53935FF")).Should().Be(named);
    }

    [Theory]
    [InlineData("PLA Basic", "Bambu PLA Basic", true)]
    [InlineData("PLA Basic", "basic pla", true)]
    [InlineData("PLA Basic", "PLA", true)]
    [InlineData("PLA Basic", "PLA Matte", false)]
    [InlineData(null, "PLA Matte", true)]
    public void SameLine_AllTheWordsOfOneNameAreInTheOther(string? subBrand, string material, bool same) =>
        RfidVariantMatch.SameLine(subBrand, material).Should().Be(same);

    [Fact]
    public void Find_ATagWithoutASubBrand_IsNoMatch()
    {
        RfidVariantMatch.Find([Color("PLA Matte", "Black", "#000000")], Tray(null, "000000FF")).Should().BeNull();
    }
}
