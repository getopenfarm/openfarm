using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Tests.Parts;

public class PartFilamentSelectionValidatorTests
{
    private static async Task<(PartFilamentSelectionValidator Validator, Filament Petg)> ArrangeAsync()
    {
        var filaments = new FakeFilamentRepository();
        var petg = Filament.Create(Guid.NewGuid(), "PETG Basic", "PETG");
        await filaments.AddAsync(petg, default);
        return (new PartFilamentSelectionValidator(filaments), petg);
    }

    [Fact]
    public async Task AnyColourOfAFilament_OfTheRequiredMaterial_Passes()
    {
        var (validator, petg) = await ArrangeAsync();

        await validator.Invoking(v => v.ValidateAsync("PETG", null, default, petg.Id)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task AnyColourOfAFilament_OfAnotherMaterial_IsRejected()
    {
        var (validator, petg) = await ArrangeAsync();

        await validator.Invoking(v => v.ValidateAsync("PLA", null, default, petg.Id))
            .Should().ThrowAsync<InvalidPartFilamentSelectionException>().WithMessage("*PLA*");
    }

    [Fact]
    public async Task AnyColourOfAMissingFilament_IsRejected()
    {
        var (validator, _) = await ArrangeAsync();

        await validator.Invoking(v => v.ValidateAsync("PETG", null, default, Guid.NewGuid()))
            .Should().ThrowAsync<InvalidPartFilamentSelectionException>();
    }

    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, false, true)]
    public async Task TwoChoicesAtOnce_AreRejected(bool variant, bool filament, bool profile, bool anyColor)
    {
        var (validator, petg) = await ArrangeAsync();

        await validator.Invoking(v => v.ValidateAsync("PETG", variant ? Guid.NewGuid() : null, default, filament ? petg.Id : null,
                profile ? Guid.NewGuid() : null, anyColor))
            .Should().ThrowAsync<InvalidPartFilamentSelectionException>().WithMessage("*at most one*");
    }

    [Fact]
    public async Task AnyColorAlone_Passes()
    {
        var (validator, _) = await ArrangeAsync();

        await validator.Invoking(v => v.ValidateAsync("PETG", null, default, anyColor: true)).Should().NotThrowAsync();
    }
}
