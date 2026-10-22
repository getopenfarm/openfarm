using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// The purchase fields replace what is stored, like the label: null clears one.
/// </summary>
/// <param name="Reason">Why the weight changed; kept with the history entry, and ignored when the weight did not change.</param>
/// <param name="Price">What was paid for the whole spool; null clears it.</param>
public sealed record EditSpoolCommand(
    Guid SpoolId,
    string? Label,
    decimal RemainingWeightGrams,
    string? Reason = null,
    decimal? Price = null,
    string? Supplier = null,
    string? OrderNumber = null,
    DateOnly? ReceivedOn = null);

/// <summary>
/// PUT /api/v1/spools/{id} — label, purchase info and remaining weight in one save. Weight is corrected only when it changed,
/// since a correction clears HasUsageOverrun; a change also writes a history entry, committed in the same save.
/// </summary>
public sealed class EditSpoolCommandHandler(ISpoolRepository spoolRepository, ISpoolUsageRecordRepository usageRecordRepository)
    : ICommandHandler<EditSpoolCommand, Spool>
{
    public async Task<Spool> HandleAsync(EditSpoolCommand command, CancellationToken cancellationToken)
    {
        var spool = await spoolRepository.GetByIdAsync(command.SpoolId, cancellationToken)
            ?? throw new SpoolNotFoundException(command.SpoolId);

        spool.Relabel(command.Label);
        spool.SetPurchase(command.Price, command.Supplier, command.OrderNumber, command.ReceivedOn);
        if (command.RemainingWeightGrams != spool.RemainingWeightGrams)
        {
            var before = spool.RemainingWeightGrams;
            spool.CorrectRemainingWeight(command.RemainingWeightGrams);

            // Add, not AddAsync: the entry commits with the spool below, so a stale row version is the spool's 409 and neither is saved.
            usageRecordRepository.Add(SpoolUsageRecord.Correction(spool.Id, before, command.RemainingWeightGrams, command.Reason));
        }

        await spoolRepository.SaveChangesAsync(cancellationToken);

        return spool;
    }
}
