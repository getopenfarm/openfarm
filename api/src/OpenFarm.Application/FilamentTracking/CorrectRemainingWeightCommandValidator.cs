using FluentValidation;

namespace OpenFarm.Application.FilamentTracking;

public sealed class CorrectRemainingWeightCommandValidator : AbstractValidator<CorrectRemainingWeightCommand>
{
    public CorrectRemainingWeightCommandValidator()
    {
        RuleFor(x => x.RemainingWeightGrams).GreaterThanOrEqualTo(0);
    }
}
