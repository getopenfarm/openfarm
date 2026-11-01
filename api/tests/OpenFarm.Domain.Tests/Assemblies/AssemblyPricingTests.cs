using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Domain.Tests.Assemblies;

public sealed class AssemblyPricingTests
{
    [Theory]
    [InlineData("Price", 0)]
    [InlineData("Price", 27.49)]
    [InlineData("Margin", 99.9)]
    [InlineData("Markup", 515)]
    public void AcceptsEachModeInRange(string mode, decimal value) =>
        Assert.Equal(value, new AssemblyPricing(mode, value).Value);

    [Theory]
    [InlineData("Price", -1)]
    [InlineData("Margin", 100)]
    [InlineData("Markup", -0.1)]
    [InlineData("Discount", 10)]
    public void RejectsOutOfRangeOrUnknownMode(string mode, decimal value) =>
        Assert.ThrowsAny<ArgumentException>(() => new AssemblyPricing(mode, value));

    [Theory]
    [InlineData("Commission", "Percent", -1)]
    [InlineData("Commission", "Percent", 100)]
    [InlineData("Listing fee", "Fixed", -0.01)]
    [InlineData("", "Fixed", 1)]
    [InlineData("Ads", "Monthly", 1)]
    public void FeeRejectsInvalidLines(string name, string type, decimal amount) =>
        Assert.ThrowsAny<ArgumentException>(() => new Fee(name, type, amount));

    [Fact]
    public void FeeProfileSumsPercentAndFixedFeesLikeTheWorkbook()
    {
        var shopee = FeeProfile.Create("Shopee", [
            new Fee("Commission", "Percent", 17.28m),
            new Fee("Payment", "Percent", 3.78m),
            new Fee("Offsite ads", "Percent", 10m),
            new Fee("Listing fee", "Fixed", 0.54m),
        ]);
        Assert.Equal(31.06m, shopee.FeePercent);
        Assert.Equal(0.54m, shopee.FixedFee);
        Assert.Equal(0m, FeeProfile.Create("Direct", []).FeePercent);
    }

    [Fact]
    public void FeeProfileRejectsPercentFeesTotallingHundred() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FeeProfile.Create("Greedy", [
            new Fee("Commission", "Percent", 60m),
            new Fee("Ads", "Percent", 40m),
        ]));

    [Fact]
    public void EditingFeeProfileKeepsItsIdentityAndRejectsInvalidChangesAtomically()
    {
        var profile = FeeProfile.Create("Direct", []);
        var id = profile.Id;
        profile.Edit(" Shop ", [new Fee("Payment", "Percent", 10m)]);
        Assert.Equal(id, profile.Id);
        Assert.Equal("Shop", profile.Name);
        Assert.Throws<ArgumentOutOfRangeException>(() => profile.Edit("Invalid", [new Fee("A", "Percent", 60m), new Fee("B", "Percent", 40m)]));
        Assert.Equal("Shop", profile.Name);
        Assert.Equal(10m, profile.FeePercent);
        Assert.Throws<ArgumentException>(() => profile.Edit(new string('A', 101), []));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void FailureAllowanceStaysBelowHundredPercent(decimal percent) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FarmSettings.Create().SetFailureAllowance(percent));
}
