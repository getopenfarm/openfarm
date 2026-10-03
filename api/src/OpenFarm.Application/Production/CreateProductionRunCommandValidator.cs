using FluentValidation;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed class CreateProductionRunCommandValidator : AbstractValidator<CreateProductionRunCommand>
{
    public CreateProductionRunCommandValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new RunLineInputValidator());
    }
}

public sealed class RunLineInputValidator : AbstractValidator<RunLineInput>
{
    public RunLineInputValidator()
    {
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(1).When(x => x.Quantity is not null);
        RuleFor(x => x.BackfillPrinterCap).GreaterThanOrEqualTo(1);
        RuleFor(x => x.DefaultPinnedPrinterId)
            .NotNull()
            .When(x => x.DefaultAssignmentMode == AssignmentMode.Pinned)
            .WithMessage("A Pinned line requires a DefaultPinnedPrinterId.");
    }
}
