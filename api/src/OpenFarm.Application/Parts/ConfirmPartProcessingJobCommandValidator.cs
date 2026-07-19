using FluentValidation;

namespace OpenFarm.Application.Parts;

public sealed class ConfirmPartProcessingJobCommandValidator : AbstractValidator<ConfirmPartProcessingJobCommand>
{
    public ConfirmPartProcessingJobCommandValidator()
    {
        RuleForEach(x => x.FilamentRequirementOverrides)
            .Must(o => o.NarrowFilamentVariantId is null || o.NarrowFilamentProfileId is null)
            .WithMessage("At most one of narrowFilamentVariantId/narrowFilamentProfileId may be set per override entry.");
    }
}
