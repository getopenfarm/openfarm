using FluentValidation;

namespace OpenFarm.Application.FilamentTracking;

public sealed class CreateVendorCommandValidator : AbstractValidator<CreateVendorCommand>
{
    public CreateVendorCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
    }
}
