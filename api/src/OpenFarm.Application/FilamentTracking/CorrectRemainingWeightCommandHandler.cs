using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>FR-012 — manual override at any time; always resets HasUsageOverrun.</summary>
public sealed class CorrectRemainingWeightCommandHandler(ISpoolRepository spoolRepository)
    : ICommandHandler<CorrectRemainingWeightCommand, Spool>
{
    public async Task<Spool> HandleAsync(CorrectRemainingWeightCommand command, CancellationToken cancellationToken)
    {
        var spool = await spoolRepository.GetByIdAsync(command.SpoolId, cancellationToken)
            ?? throw new SpoolNotFoundException(command.SpoolId);

        spool.CorrectRemainingWeight(command.RemainingWeightGrams);
        await spoolRepository.SaveChangesAsync(cancellationToken);

        return spool;
    }
}
