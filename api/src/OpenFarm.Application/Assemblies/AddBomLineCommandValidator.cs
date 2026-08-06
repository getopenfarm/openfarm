using FluentValidation;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

public sealed class AddBomLineCommandValidator : AbstractValidator<AddBomLineCommand>
{
    public AddBomLineCommandValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0);

        RuleFor(x => x)
            .Must(HaveAValidQuantityForItsTargetKind)
            .WithMessage("Quantity must be a whole number of at least 1 for Part/Assembly targets.");
    }

    private static bool HaveAValidQuantityForItsTargetKind(AddBomLineCommand command) =>
        command.TargetKind == BomLineTargetKind.NonPrintedComponent
            ? command.Quantity > 0
            : command.Quantity >= 1 && command.Quantity == Math.Truncate(command.Quantity);
}
