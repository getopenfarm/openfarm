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

/// <summary>FR-004a/FR-004b: whole number ≥ 1 for Part/Assembly targets; fractional &gt; 0 allowed for NonPrintedComponent.</summary>
public sealed class AssemblyLineInputValidator : AbstractValidator<AssemblyLineInput>
{
    public AssemblyLineInputValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0);

        RuleFor(x => x)
            .Must(HaveAValidQuantityForItsTargetKind)
            .WithMessage("Quantity must be a whole number of at least 1 for Part/Assembly targets.");
    }

    private static bool HaveAValidQuantityForItsTargetKind(AssemblyLineInput line) =>
        line.TargetKind == BomLineTargetKind.NonPrintedComponent
            ? line.Quantity > 0
            : line.Quantity >= 1 && line.Quantity == Math.Truncate(line.Quantity);
}
