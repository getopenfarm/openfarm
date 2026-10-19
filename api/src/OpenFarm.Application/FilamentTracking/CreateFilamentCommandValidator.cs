using FluentValidation;

namespace OpenFarm.Application.FilamentTracking;

public sealed class CreateFilamentCommandValidator : AbstractValidator<CreateFilamentCommand>
{
    public CreateFilamentCommandValidator()
    {
        RuleFor(x => x.VendorId).NotEmpty();
        RuleFor(x => x.Material).NotEmpty();
        RuleFor(x => x.PricePerKg).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaterialType).NotEmpty();
        RuleFor(x => x.Variants).NotEmpty();
        RuleForEach(x => x.Variants).ChildRules(variant =>
        {
            variant.RuleFor(v => v.ColorName).NotEmpty();
            variant.RuleFor(v => v.ColorHex).NotEmpty();
            variant.RuleFor(v => v.PricePerKg).GreaterThanOrEqualTo(0);
        });
    }
}
