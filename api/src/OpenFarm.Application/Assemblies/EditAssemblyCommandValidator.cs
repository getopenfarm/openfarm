using FluentValidation;

namespace OpenFarm.Application.Assemblies;

public sealed class EditAssemblyCommandValidator : AbstractValidator<EditAssemblyCommand>
{
    public EditAssemblyCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
    }
}
