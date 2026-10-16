using FluentValidation;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

public sealed class CreateAssemblyCommandValidator : AbstractValidator<CreateAssemblyCommand>
{
    public CreateAssemblyCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new AssemblyLineInputValidator());
    }
}

/// <summary>FR-004a/FR-004b: whole number ≥ 1 for every target kind.</summary>
public sealed class AssemblyLineInputValidator : AbstractValidator<AssemblyLineInput>
{
    public AssemblyLineInputValidator()
    {
        RuleFor(x => x.Quantity)
            .Must(q => q >= 1 && q == Math.Truncate(q))
            .WithMessage("Quantity must be a whole number of at least 1.");
    }
}
