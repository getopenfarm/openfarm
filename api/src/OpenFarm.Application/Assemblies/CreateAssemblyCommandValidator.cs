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

/// <summary>Material quantities may be measured; the referenced catalog unit validates whole pieces.</summary>
public sealed class AssemblyLineInputValidator : AbstractValidator<AssemblyLineInput>
{
    public AssemblyLineInputValidator()
    {
        RuleFor(x => x.Quantity)
            .Must((line, q) => q > 0 && (line.TargetKind == BomLineTargetKind.NonPrintedComponent || q == Math.Truncate(q)))
            .WithMessage("Quantity must be positive; parts and assemblies require whole numbers.");
    }
}
