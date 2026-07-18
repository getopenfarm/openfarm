using FluentValidation;

namespace OpenFarm.Application.FilamentTracking;

public sealed class AddFilamentVariantCommandValidator : AbstractValidator<AddFilamentVariantCommand>
{
    public AddFilamentVariantCommandValidator()
    {
        RuleFor(x => x.FilamentId).NotEmpty();
        RuleFor(x => x.ColorName).NotEmpty();
        RuleFor(x => x.ColorHex).NotEmpty();
    }
}
