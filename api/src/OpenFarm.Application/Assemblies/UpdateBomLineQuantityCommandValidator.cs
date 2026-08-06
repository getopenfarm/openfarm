using FluentValidation;

namespace OpenFarm.Application.Assemblies;

/// <summary>
/// A quantity-only change — its target (and thus the FR-004a/b whole-number-vs-fractional rule that
/// depends on target kind) is immutable once the line exists, so the definitive check runs in
/// <see cref="OpenFarm.Domain.Assemblies.BomLine.UpdateQuantity"/> against the already-persisted
/// kind. This gate only rejects the trivially-invalid case that never needs a DB round trip.
/// </summary>
public sealed class UpdateBomLineQuantityCommandValidator : AbstractValidator<UpdateBomLineQuantityCommand>
{
    public UpdateBomLineQuantityCommandValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}
