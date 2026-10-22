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

    private async Task<Guid> VariantAsync()
    {
        var filament = Filament.Create(Guid.NewGuid(), "PLA Basic", "PLA");
        var variant = filament.AddVariant("Black", "#000000");
        await _filaments.AddAsync(filament, CancellationToken.None);
        return variant.Id;
    }

    private RegisterSpoolCommandHandler Handler() => new(_spools, _filaments);

    private static RegisterSpoolCommand Command(Guid variantId, int quantity = 1) => new(
        variantId, "Shelf B", 750, 700, quantity,
        Price: 18.5m, Supplier: "Filament Shop", OrderNumber: "PO-1042", ReceivedOn: new DateOnly(2026, 10, 20));

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
    public async Task WithoutARemainingWeight_EachSpoolStartsFull()
    {
        var variantId = await VariantAsync();

        var added = await Handler().HandleAsync(new RegisterSpoolCommand(variantId, null, 1000, null, Quantity: 2), CancellationToken.None);

        added.Should().OnlyContain(s => s.RemainingWeightGrams == 1000 && s.Price == null && s.Supplier == null);
    }

    [Fact]
    public async Task AnUnknownColour_IsNotFound_AndAddsNothing()
    {
        var register = () => Handler().HandleAsync(Command(Guid.NewGuid(), quantity: 3), CancellationToken.None);

        await register.Should().ThrowAsync<FilamentVariantNotFoundException>();
        (await _spools.ListAsync(null, null, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task OneInvalidValue_AddsNoSpools()
    {
        var variantId = await VariantAsync();

        var register = () => Handler().HandleAsync(Command(variantId, quantity: 3) with { Price = -1m }, CancellationToken.None);

        await register.Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await _spools.ListAsync(null, null, CancellationToken.None)).Should().BeEmpty();
    }
}
