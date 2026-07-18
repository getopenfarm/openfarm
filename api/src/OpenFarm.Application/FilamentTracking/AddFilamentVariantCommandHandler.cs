using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed class AddFilamentVariantCommandHandler(IFilamentRepository filamentRepository)
    : ICommandHandler<AddFilamentVariantCommand, FilamentVariant>
{
    public async Task<FilamentVariant> HandleAsync(AddFilamentVariantCommand command, CancellationToken cancellationToken)
    {
        var filament = await filamentRepository.GetByIdAsync(command.FilamentId, cancellationToken)
            ?? throw new FilamentNotFoundException(command.FilamentId);

        // Backed by the unique (FilamentId, ColorName) index (data-model.md) — checked here too so
        // the 409 surfaces as a typed Domain exception rather than a raw DB constraint failure.
        if (filament.Variants.Any(v => string.Equals(v.ColorName, command.ColorName, StringComparison.Ordinal)))
        {
            throw new DuplicateFilamentVariantColorException(command.FilamentId, command.ColorName);
        }

        var variant = filament.AddVariant(command.ColorName, command.ColorHex);
        await filamentRepository.AddVariantAsync(variant, cancellationToken);

        return variant;
    }
}
