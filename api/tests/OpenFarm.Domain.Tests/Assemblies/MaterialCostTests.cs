using FluentAssertions;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Domain.Tests.Assemblies;

public class MaterialCostTests
{
    private static NonPrintedComponent BubbleWrap()
    {
        var item = NonPrintedComponent.Create("Bubble wrap", null);
        item.Configure(new CatalogPricing(Category: "Packaging", Unit: "SquareMetre", PricingMode: "Package",
            PackageLabel: "roll", PackagePrice: 40m, Length: 100, LengthUnit: "m", Width: 100, WidthUnit: "cm"));
        return item;
    }

    [Fact]
    public void RollDimensionsAndCutDimensionsCalculateCostWithoutRounding()
    {
        var item = BubbleWrap();
        var cut = new AssemblyCostInput(item.Id, "FinalProduct", InputMode: "Dimensions",
            Length: 50, LengthUnit: "cm", Width: 200, WidthUnit: "mm");
        item.PackageQuantity.Should().Be(100m);
        item.UnitCost.Should().Be(0.40m);
        cut.Usage(item).Should().Be(0.1m);
        (cut.Usage(item) * item.UnitCost).Should().Be(0.04m);
        (cut with { Pieces = 3 }).Usage(item).Should().Be(0.3m);
        item.Configure(item.Pricing with { Width = 50 });
        item.UnitCost.Should().Be(0.8m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(40)]
    public void PackagePricePreservesUnknownAndZero(int? amount)
    {
        decimal? price = amount;
        VerifyPackagePrice(price);
    }

    private static void VerifyPackagePrice(decimal? price)
    {
        var pricing = new CatalogPricing(Unit: "Millilitre", PricingMode: "Package", PackageLabel: "bottle", PackagePrice: price, PackageQuantity: 500);
        pricing.EffectiveRate().Should().Be(price / 500);
    }

    [Fact]
    public void SmallRatesRetainPrecision()
    {
        var pricing = new CatalogPricing(Unit: "SquareMetre", PricingMode: "Package", PackageLabel: "roll", PackagePrice: 0.01m, PackageQuantity: 1000);
        pricing.EffectiveRate().Should().Be(0.00001m);
    }

    [Fact]
    public void PackageLabelIsOptional()
    {
        var pricing = new CatalogPricing(Unit: "Piece", PricingMode: "Package", PackagePrice: 8m, PackageQuantity: 200);
        pricing.EffectiveRate().Should().Be(0.04m);
        var item = NonPrintedComponent.Create("Screw", null);
        item.Configure(pricing);
        item.PackageLabel.Should().BeNull();
        item.UnitCost.Should().Be(0.04m);
    }

    [Fact]
    public void LabourUsesMinutesAndHourlyRate()
    {
        var item = NonPrintedComponent.Create("Assembly labour", null);
        item.Configure(new CatalogPricing("Service", "Labour", "Hour", DirectRate: 20));
        (new AssemblyCostInput(item.Id, Quantity: 6).Usage(item) * item.UnitCost).Should().Be(2m);
    }

    [Fact]
    public void PiecesRequireWholeUsageAndServicesCannotEnterBom()
    {
        var item = NonPrintedComponent.Create("Screw", 0.05m);
        item.Invoking(i => i.ValidateUsage(0.5m, bom: true)).Should().Throw<ArgumentException>();
        item.Configure(new CatalogPricing("Service", "Labour", "Application", DirectRate: 2));
        item.Invoking(i => i.ValidateUsage(1, bom: true)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RejectsInvalidPackageAndDimensionCombinations()
    {
        new CatalogPricing(Unit: "SquareMetre", PricingMode: "Package", PackageLabel: "roll", PackagePrice: -1, PackageQuantity: 100)
            .Invoking(p => p.Validate()).Should().Throw<ArgumentException>();
        new CatalogPricing(PricingMode: "Package", PackageLabel: "pack", PackageQuantity: 0)
            .Invoking(p => p.Validate()).Should().Throw<ArgumentException>();
        new CatalogPricing(Unit: "SquareMetre", PricingMode: "Package", PackageLabel: "roll", Length: 100)
            .Invoking(p => p.Validate()).Should().Throw<ArgumentException>();
        new CatalogPricing(Unit: "SquareMetre", PricingMode: "Package", PackageLabel: "roll", Length: 100, Width: -1)
            .Invoking(p => p.Validate()).Should().Throw<ArgumentException>();
        new CatalogPricing("Service", Unit: "SquareMetre").Invoking(p => p.Validate()).Should().Throw<ArgumentException>();
        var item = BubbleWrap();
        item.Invoking(i => new AssemblyCostInput(i.Id, InputMode: "Dimensions", Length: 50, Width: 20, Pieces: 0).Usage(i))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CostLineEditsPreserveIdentityAndRejectBomDuplicates()
    {
        var item = BubbleWrap();
        var assembly = Assembly.Create("Product", null, [new BomLineSpec(BomLineTargetKind.Part, Guid.NewGuid(), null, null, 1)]);
        assembly.ReplaceAdditionalCosts([new(item.Id)]).Should().HaveCount(1);
        var id = assembly.AdditionalCosts[0].Id;
        assembly.ReplaceAdditionalCosts([new(item.Id, Quantity: 2)]).Should().BeEmpty();
        assembly.AdditionalCosts[0].Id.Should().Be(id);
        assembly.AdditionalCosts[0].Quantity.Should().Be(2);
        assembly.Invoking(a => a.ReplaceAdditionalCosts([new(item.Id), new(item.Id)])).Should().Throw<ArgumentException>();
        assembly.ReplaceLines([new BomLineEdit(null, new BomLineSpec(BomLineTargetKind.NonPrintedComponent, null, null, item.Id, 1))]);
        assembly.Invoking(a => a.ValidateCostDuplicates()).Should().Throw<ArgumentException>();
    }
}
