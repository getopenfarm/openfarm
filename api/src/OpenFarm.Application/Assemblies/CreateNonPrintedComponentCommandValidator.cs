using FluentValidation;

namespace OpenFarm.Application.Assemblies;

public sealed class CreateNonPrintedComponentCommandValidator : AbstractValidator<CreateNonPrintedComponentCommand>
{
    public CreateNonPrintedComponentCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
    }
}
