using FluentAssertions;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Tests.Production.Fakes;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Tests.FilamentTracking;

public class RegisterSpoolCommandHandlerTests
{
    private readonly InMemorySpoolRepository _spools = new();
    private readonly FakeFilamentRepository _filaments = new();

    private async Task<Guid> VariantAsync(string color = "Black")
    {
        var filament = Filament.Create(Guid.NewGuid(), "PLA Basic", "PLA");
        var variant = filament.AddVariant(color, "#000000");
        await _filaments.AddAsync(filament, CancellationToken.None);
        return variant.Id;
    }

    private RegisterSpoolCommandHandler Handler() => new(_spools, _filaments);

    private static RegisterSpoolCommand Command(Guid variantId, int quantity = 1, decimal? price = 18.5m) => new(
        [new RegisterSpoolLine(variantId, 750, quantity, RemainingWeightGrams: 700, Price: price, Label: "Shelf B")],
        Supplier: "Filament Shop", OrderNumber: "PO-1042", ReceivedOn: new DateOnly(2026, 10, 20));

    [Fact]
    public async Task AQuantity_AddsThatManyIndependentSpools_EachWithTheSameDetails()
    {
        var variantId = await VariantAsync();

        var added = await Handler().HandleAsync(Command(variantId, quantity: 3), CancellationToken.None);

        added.Should().HaveCount(3);
        added.Select(s => s.Id).Distinct().Should().HaveCount(3);
        added.Should().OnlyContain(s =>
            s.FilamentVariantId == variantId && s.Label == "Shelf B" && s.SpoolWeightGrams == 750 && s.RemainingWeightGrams == 700
            && s.Price == 18.5m && s.Supplier == "Filament Shop" && s.OrderNumber == "PO-1042" && s.ReceivedOn == new DateOnly(2026, 10, 20));
        (await _spools.ListAsync(null, null, CancellationToken.None)).Should().HaveCount(3);
    }

    [Fact]
    public async Task EachLine_AddsItsOwnColor_WithTheDeliverysSharedDetails()
    {
        var black = await VariantAsync();
        var white = await VariantAsync("White");

        var added = await Handler().HandleAsync(
            new RegisterSpoolCommand([new RegisterSpoolLine(black, 1000, 2), new RegisterSpoolLine(white, 500, 1, Price: 9m)], Supplier: "Shop"),
            CancellationToken.None);

        added.Where(s => s.FilamentVariantId == black).Should().HaveCount(2).And.OnlyContain(s => s.SpoolWeightGrams == 1000 && s.Price == null);
        added.Where(s => s.FilamentVariantId == white).Should().ContainSingle().Which.Price.Should().Be(9m);
        added.Should().OnlyContain(s => s.Supplier == "Shop");
    }

    [Fact]
    public async Task WithoutARemainingWeight_EachSpoolStartsFull()
    {
        var variantId = await VariantAsync();

        var added = await Handler().HandleAsync(new RegisterSpoolCommand([new RegisterSpoolLine(variantId, 1000, 2)]), CancellationToken.None);

        added.Should().OnlyContain(s => s.RemainingWeightGrams == 1000 && s.Price == null && s.Supplier == null);
    }

    [Fact]
    public async Task AnUnknownColourOnAnyLine_IsNotFound_AndAddsNothing()
    {
        var variantId = await VariantAsync();
        var command = new RegisterSpoolCommand([new RegisterSpoolLine(variantId, 1000, 3), new RegisterSpoolLine(Guid.NewGuid(), 1000)]);

        var register = () => Handler().HandleAsync(command, CancellationToken.None);

        await register.Should().ThrowAsync<FilamentVariantNotFoundException>();
        (await _spools.ListAsync(null, null, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task OneInvalidValue_AddsNoSpools()
    {
        var variantId = await VariantAsync();

        var register = () => Handler().HandleAsync(Command(variantId, quantity: 3, price: -1m), CancellationToken.None);

        await register.Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await _spools.ListAsync(null, null, CancellationToken.None)).Should().BeEmpty();
    }
}
