using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed class RegisterSpoolCommandHandler(
    ISpoolRepository spoolRepository,
    IFilamentRepository filamentRepository) : ICommandHandler<RegisterSpoolCommand, IReadOnlyList<Spool>>
{
    public async Task<IReadOnlyList<Spool>> HandleAsync(RegisterSpoolCommand command, CancellationToken cancellationToken)
    {
        var variant = await filamentRepository.FindVariantByIdAsync(command.FilamentVariantId, cancellationToken)
            ?? throw new FilamentVariantNotFoundException(command.FilamentVariantId);

        // Built before anything is saved, so one invalid value adds none of them.
        var spools = Enumerable.Range(0, command.Quantity).Select(_ => NewSpool(variant.Id, command)).ToList();
        if (command.RfidId is { } rfidId)
        {
            if ((await spoolRepository.FindByRfidIdsAsync([rfidId.Trim()], cancellationToken)).Count > 0)
                throw new SpoolRfidConflictException("RFID spool already registered.");
            spools.Single().AttachRfid(rfidId);
        }

        await spoolRepository.AddRangeAsync(spools, cancellationToken);

        return spools;
    }

    private static Spool NewSpool(Guid filamentVariantId, RegisterSpoolCommand command)
    {
        var spool = Spool.Create(
            filamentVariantId,
            command.Label,
            command.SpoolWeightGrams,
            command.RemainingWeightGrams ?? command.SpoolWeightGrams);
        spool.SetPurchase(command.Price, command.Supplier, command.OrderNumber, command.ReceivedOn);
        return spool;
    }
}
