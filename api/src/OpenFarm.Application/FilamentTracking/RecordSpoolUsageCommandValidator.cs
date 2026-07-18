using FluentValidation;

namespace OpenFarm.Application.FilamentTracking;

public sealed class RecordSpoolUsageCommandValidator : AbstractValidator<RecordSpoolUsageCommand>
{
    public RecordSpoolUsageCommandValidator()
    {
        RuleFor(x => x.GramsConsumed).GreaterThan(0);
    }
}
