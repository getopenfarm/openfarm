using FluentValidation;

namespace OpenFarm.Application.Assemblies;

public sealed class EditNonPrintedComponentCommandValidator : AbstractValidator<EditNonPrintedComponentCommand>
{
    public EditNonPrintedComponentCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
    }
}
