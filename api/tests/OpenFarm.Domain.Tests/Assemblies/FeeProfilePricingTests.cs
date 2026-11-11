using FluentAssertions;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Domain.Tests.Assemblies;

/// <summary>Sheet H0001 of the pricing workbook: cost 4.47, Shopee 31.06% + 0.54 per order (the web's former pricing.test.ts).</summary>
public sealed class FeeProfilePricingTests
{
    private const decimal Cost = 4.47m;
    private static ProfilePricing Shopee(AssemblyPricing? pricing) => FeeProfilePricing.Calculate(Cost, 31.06m, 0.54m, pricing);
    private static ProfilePricing Direct(AssemblyPricing? pricing) => FeeProfilePricing.Calculate(Cost, 0, 0, pricing);

    [Fact]
    public void ReadsAFixedPriceLikeTheWorkbook()
    {
        var r = Shopee(new("Price", 27.49m));

        r.Price.Should().Be(27.49m);
        r.Fees.Should().BeApproximately(9.08m, 0.005m);
        r.Profit.Should().BeApproximately(13.94m, 0.005m);
        r.Margin.Should().BeApproximately(0.507m, 0.0005m);
        r.Markup.Should().BeApproximately(5.15m, 0.005m);
        r.BreakEven.Should().BeApproximately(7.27m, 0.005m);
        r.MaxDiscount.Should().BeApproximately(0.736m, 0.0005m);
    }

    [Fact]
    public void PricesFromMarginAfterFees_PerFeeProfile()
    {
        Shopee(new("Margin", 45)).Price.Should().Be(20.93m);
        Direct(new("Margin", 45)).Price.Should().Be(8.13m);
    }

    [Fact]
    public void PricesFromMarkupOnCost_TheSameOnEveryFeeProfile() => Shopee(new("Markup", 515)).Price.Should().Be(27.49m);

    [Fact]
    public void KeepsAllOfThePriceOnAProfileWithoutFees()
    {
        var r = Direct(new("Price", 27.49m));
        r.Profit.Should().BeApproximately(23.02m, 0.005m);
        r.Margin.Should().BeApproximately(0.837m, 0.0005m);
    }

    [Fact]
    public void FlagsAMarginTheFeesMakeUnreachable()
    {
        var r = Shopee(new("Margin", 70));
        r.Unreachable.Should().BeTrue();
        r.Price.Should().BeNull();
        r.Fees.Should().BeNull();
    }

    [Fact]
    public void StillGivesBreakEvenWithoutPricing()
    {
        var r = Shopee(null);
        r.Price.Should().BeNull();
        r.Unreachable.Should().BeFalse();
        r.BreakEven.Should().BeApproximately(7.27m, 0.005m);
    }

    [Fact]
    public void LeavesMarginAndMarkupOutWhenPriceOrCostIsZero()
    {
        var free = FeeProfilePricing.Calculate(0, 0, 0, new("Price", 0));
        free.Margin.Should().BeNull();
        free.Markup.Should().BeNull();
        free.MaxDiscount.Should().BeNull();
    }
}
