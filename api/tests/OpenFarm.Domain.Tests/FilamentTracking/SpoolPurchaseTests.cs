using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class SpoolPurchaseTests
{
    private static Spool NewSpool(decimal capacity = 1000, decimal? remaining = null) =>
        Spool.Create(Guid.NewGuid(), null, capacity, remaining ?? capacity);

    [Fact]
    public void ANewSpool_HasNoPurchaseInfoAndNoPrice()
    {
        var spool = NewSpool();

        spool.Price.Should().BeNull();
        spool.Supplier.Should().BeNull();
        spool.OrderNumber.Should().BeNull();
        spool.ReceivedOn.Should().BeNull();
        spool.PricePerKg.Should().BeNull();
    }

    [Fact]
    public void SetPurchase_KeepsWhatItWasGiven()
    {
        var spool = NewSpool();

        spool.SetPurchase(18.5m, "Filament Shop", "PO-1042", new DateOnly(2026, 10, 20));

        spool.Price.Should().Be(18.5m);
        spool.Supplier.Should().Be("Filament Shop");
        spool.OrderNumber.Should().Be("PO-1042");
        spool.ReceivedOn.Should().Be(new DateOnly(2026, 10, 20));
    }

    [Fact]
    public void SetPurchase_ZeroIsARealPrice_NotUnknown()
    {
        var spool = NewSpool();

        spool.SetPurchase(0m, null, null, null);

        spool.Price.Should().Be(0m);
        spool.PricePerKg.Should().Be(0m);
    }

    [Fact]
    public void SetPurchase_WithNulls_ClearsEverything()
    {
        var spool = NewSpool();
        spool.SetPurchase(18.5m, "Filament Shop", "PO-1042", new DateOnly(2026, 10, 20));

        spool.SetPurchase(null, null, null, null);

        spool.Price.Should().BeNull();
        spool.Supplier.Should().BeNull();
        spool.OrderNumber.Should().BeNull();
        spool.ReceivedOn.Should().BeNull();
        spool.PricePerKg.Should().BeNull();
    }

    [Fact]
    public void SetPurchase_NegativePrice_Throws_AndKeepsWhatWasThere()
    {
        var spool = NewSpool();
        spool.SetPurchase(20m, "Filament Shop", null, null);

        var change = () => spool.SetPurchase(-1m, "Other", null, null);

        change.Should().Throw<ArgumentOutOfRangeException>();
        spool.Price.Should().Be(20m);
        spool.Supplier.Should().Be("Filament Shop");
    }

    [Theory]
    [InlineData("  Filament Shop  ", "Filament Shop")]
    [InlineData("   ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SetPurchase_TrimsText_AndTurnsBlankIntoNull(string? text, string? expected)
    {
        var spool = NewSpool();

        spool.SetPurchase(null, text, text, null);

        spool.Supplier.Should().Be(expected);
        spool.OrderNumber.Should().Be(expected);
    }

    [Fact]
    public void SetPurchase_TextAtTheLimit_IsKept_AndOverItThrows()
    {
        var spool = NewSpool();
        var atLimit = new string('x', Spool.MaxPurchaseTextLength);
        var overLimit = new string('x', Spool.MaxPurchaseTextLength + 1);

        spool.SetPurchase(null, atLimit, atLimit, null);
        var supplierTooLong = () => spool.SetPurchase(null, overLimit, null, null);
        var orderTooLong = () => spool.SetPurchase(null, null, overLimit, null);

        spool.Supplier.Should().HaveLength(Spool.MaxPurchaseTextLength);
        supplierTooLong.Should().Throw<ArgumentException>();
        orderTooLong.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(20, 1000, 20)]
    [InlineData(10, 500, 20)]
    [InlineData(18.5, 750, 24.6667)]
    public void PricePerKg_IsThePricePaidOverTheSpoolsWeightInKilograms(double price, double capacity, double expected)
    {
        var spool = NewSpool((decimal)capacity);

        spool.SetPurchase((decimal)price, null, null, null);

        spool.PricePerKg.Should().BeApproximately((decimal)expected, 0.0001m);
    }

    [Fact]
    public void PricePerKg_UsesTheFullSpoolWeight_NotWhatIsLeft()
    {
        var spool = NewSpool(capacity: 1000, remaining: 250);

        spool.SetPurchase(20m, null, null, null);

        spool.PricePerKg.Should().Be(20m);
    }
}
