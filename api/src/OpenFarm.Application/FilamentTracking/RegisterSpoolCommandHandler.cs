using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed class RegisterSpoolCommandHandler(
    ISpoolRepository spoolRepository,
    IFilamentRepository filamentRepository) : ICommandHandler<RegisterSpoolCommand, IReadOnlyList<Spool>>
{
    public async Task<IReadOnlyList<Spool>> HandleAsync(RegisterSpoolCommand command, CancellationToken cancellationToken)
    {
        foreach (var variantId in command.Lines.Select(l => l.FilamentVariantId).Distinct())
        {
            _ = await filamentRepository.FindVariantByIdAsync(variantId, cancellationToken)
                ?? throw new FilamentVariantNotFoundException(variantId);
        }

        // Built before anything is saved, so one invalid value adds none of them.
        var spools = command.Lines
            .SelectMany(line => Enumerable.Range(0, line.Quantity).Select(_ => NewSpool(line, command)))
            .ToList();
        if (command.RfidId is { } rfidId)
        {
            if ((await spoolRepository.FindByRfidIdsAsync([rfidId.Trim()], cancellationToken)).Count > 0)
                throw new SpoolRfidConflictException("RFID spool already registered.");
            spools.Single().AttachRfid(rfidId);
        }

        await spoolRepository.AddRangeAsync(spools, cancellationToken);

        return spools;
    }

    private static Spool NewSpool(RegisterSpoolLine line, RegisterSpoolCommand command)
    {
        var spool = Spool.Create(line.FilamentVariantId, line.Label, line.SpoolWeightGrams, line.RemainingWeightGrams ?? line.SpoolWeightGrams);
        spool.SetPurchase(line.Price, command.Supplier, command.OrderNumber, command.ReceivedOn);
        return spool;
    }
}
