using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>FR-007 — return to storage (keeps RemainingWeightGrams) or mark finished. 409 on a concurrent conflicting change.</summary>
public sealed class UnloadSpoolCommandHandler(ISpoolRepository spoolRepository) : ICommandHandler<UnloadSpoolCommand, Spool>
{
    public async Task<Spool> HandleAsync(UnloadSpoolCommand command, CancellationToken cancellationToken)
    {
        var spool = await spoolRepository.GetByIdAsync(command.SpoolId, cancellationToken)
            ?? throw new SpoolNotFoundException(command.SpoolId);

        switch (command.Action)
        {
            case UnloadSpoolAction.ReturnToStorage:
                spool.ReturnToStorage();
                break;
            case UnloadSpoolAction.MarkFinished:
                spool.MarkFinished();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }

        await spoolRepository.SaveChangesAsync(cancellationToken);

        return spool;
    }
}
