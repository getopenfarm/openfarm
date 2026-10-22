using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed record RestoreSpoolCommand(Guid SpoolId);

/// <summary>
/// PUT /api/v1/spools/{id}/restore — a finished spool back to storage, with the grams its write-off took unless it was
/// re-weighed since. 409 on a concurrent conflicting change.
/// </summary>
public sealed class RestoreSpoolCommandHandler(ISpoolRepository spoolRepository, ISpoolUsageRecordRepository usageRecordRepository)
    : ICommandHandler<RestoreSpoolCommand, Spool>
{
    public async Task<Spool> HandleAsync(RestoreSpoolCommand command, CancellationToken cancellationToken)
    {
        var spool = await spoolRepository.GetByIdAsync(command.SpoolId, cancellationToken)
            ?? throw new SpoolNotFoundException(command.SpoolId);

        // Add, not AddAsync: the entry commits with the spool below, so a re-weigh that raced the read is the spool's 409.
        if (spool.Restore(await usageRecordRepository.GetLatestAsync(spool.Id, cancellationToken)) is { } restore)
            usageRecordRepository.Add(restore);

        await spoolRepository.SaveChangesAsync(cancellationToken);

        return spool;
    }
}
