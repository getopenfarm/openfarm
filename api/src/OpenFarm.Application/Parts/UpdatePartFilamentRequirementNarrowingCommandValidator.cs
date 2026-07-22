using FluentValidation;

namespace OpenFarm.Application.Parts;

public sealed class UpdatePartFilamentRequirementNarrowingCommandValidator : AbstractValidator<UpdatePartFilamentRequirementNarrowingCommand>
{
    public UpdatePartFilamentRequirementNarrowingCommandValidator()
    {
        // Mirrors ConfirmPartProcessingJobCommandValidator's existing rule (data-model.md).
        RuleFor(x => x)
            .Must(x => x.NarrowFilamentVariantId is null || x.NarrowFilamentProfileId is null)
            .WithMessage("At most one of narrowFilamentVariantId/narrowFilamentProfileId may be set.");
    }
}
