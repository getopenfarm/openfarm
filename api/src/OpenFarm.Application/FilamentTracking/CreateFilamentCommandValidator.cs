using FluentValidation;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed class CreateFilamentCommandValidator : AbstractValidator<CreateFilamentCommand>
{
    public CreateFilamentCommandValidator()
    {
        RuleFor(x => x.VendorId).NotEmpty();
        RuleFor(x => x.Material).NotEmpty();
        RuleFor(x => x.PricePerKg).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LowStockGrams).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LowStockDays).InclusiveBetween(0, Filament.MaxLowStockDays).When(x => x.LowStockDays is not null);
        RuleFor(x => x.MaterialType).NotEmpty();
        RuleFor(x => x.Variants).NotEmpty();
        RuleForEach(x => x.Variants).ChildRules(variant =>
        {
            variant.RuleFor(v => v.ColorName).NotEmpty();
            variant.RuleFor(v => v.ColorHex).NotEmpty();
            variant.RuleFor(v => v.PricePerKg).GreaterThanOrEqualTo(0);
            variant.RuleFor(v => v.LowStockGrams).GreaterThanOrEqualTo(0);
            variant.RuleFor(v => v.LowStockDays).InclusiveBetween(0, Filament.MaxLowStockDays).When(v => v.LowStockDays is not null);
        });
    }
}
