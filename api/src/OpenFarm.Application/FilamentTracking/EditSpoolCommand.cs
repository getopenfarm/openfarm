using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed record EditSpoolCommand(Guid SpoolId, string? Label, decimal RemainingWeightGrams);

/// <summary>PUT /api/v1/spools/{id} — label and remaining weight in one save. Weight is corrected only when it changed, since a correction clears HasUsageOverrun.</summary>
public sealed class EditSpoolCommandHandler(ISpoolRepository spoolRepository) : ICommandHandler<EditSpoolCommand, Spool>
{
    public async Task<Spool> HandleAsync(EditSpoolCommand command, CancellationToken cancellationToken)
    {
        var spool = await spoolRepository.GetByIdAsync(command.SpoolId, cancellationToken)
            ?? throw new SpoolNotFoundException(command.SpoolId);

        spool.Relabel(command.Label);
        if (command.RemainingWeightGrams != spool.RemainingWeightGrams)
        {
            spool.CorrectRemainingWeight(command.RemainingWeightGrams);
        }

        await spoolRepository.SaveChangesAsync(cancellationToken);

        return spool;
    }
}
