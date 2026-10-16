using FluentValidation;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed class AddFeedUnitCommandValidator : AbstractValidator<AddFeedUnitCommand>
{
    public AddFeedUnitCommandValidator()
    {
        RuleFor(x => x.Kind).Must(k => k is FeedUnitKindCatalog.AutomaticFilamentReloader or FeedUnitKindCatalog.ExternalSpool)
            .WithMessage("Kind must be 'AutomaticFilamentReloader' or 'ExternalSpool'.");

        RuleFor(x => x.StartingSlotPosition).Must((x, position) => position is null
            || position >= 0 && position < x.SlotCount)
            .WithMessage("Starting slot must be within the declared slot count.");
        RuleFor(x => x.Name).NotEmpty().When(x => x.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader);
        RuleFor(x => x.SlotCount).NotNull().GreaterThanOrEqualTo(1).LessThanOrEqualTo(100).When(x => x.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader);
    }
}
