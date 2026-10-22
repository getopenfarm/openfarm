using FluentValidation;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed class RegisterSpoolCommandValidator : AbstractValidator<RegisterSpoolCommand>
{
    public RegisterSpoolCommandValidator()
    {
        RuleFor(x => x.FilamentVariantId).NotEmpty();
        RuleFor(x => x.SpoolWeightGrams).GreaterThan(0);
        RuleFor(x => x.RemainingWeightGrams).GreaterThan(0).When(x => x.RemainingWeightGrams is not null);
        RuleFor(x => x.RemainingWeightGrams)
            .Must((command, remaining) => remaining is null || remaining <= command.SpoolWeightGrams)
            .WithMessage("Remaining weight cannot exceed spool weight.")
            .When(x => x.RemainingWeightGrams is not null);
        RuleFor(x => x.Quantity).InclusiveBetween(1, RegisterSpoolCommand.MaxQuantity);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).When(x => x.Price is not null);
        RuleFor(x => x.Supplier).MaximumLength(Spool.MaxPurchaseTextLength);
        RuleFor(x => x.OrderNumber).MaximumLength(Spool.MaxPurchaseTextLength);
    }
}
