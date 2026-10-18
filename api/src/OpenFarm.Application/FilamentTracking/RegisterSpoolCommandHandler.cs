using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed class RegisterSpoolCommandHandler(
    ISpoolRepository spoolRepository,
    IFilamentRepository filamentRepository) : ICommandHandler<RegisterSpoolCommand, Spool>
{
    public async Task<Spool> HandleAsync(RegisterSpoolCommand command, CancellationToken cancellationToken)
    {
        var variant = await filamentRepository.FindVariantByIdAsync(command.FilamentVariantId, cancellationToken)
            ?? throw new FilamentVariantNotFoundException(command.FilamentVariantId);

        var spool = Spool.Create(
            variant.Id,
            command.Label,
            command.SpoolWeightGrams,
            command.RemainingWeightGrams ?? command.SpoolWeightGrams);

        await spoolRepository.AddAsync(spool, cancellationToken);

        return spool;
    }
}
