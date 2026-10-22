using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed record AttachSpoolRfidCommand(Guid SpoolId, string RfidId);

/// <summary>
/// PUT /api/v1/spools/{id}/rfid — ties an untagged spool already in inventory to the tag a printer reads, so a spool loaded
/// before RFID assist is not registered a second time. 409 when another active spool has the tag, or this one has another.
/// </summary>
public sealed class AttachSpoolRfidCommandHandler(ISpoolRepository spoolRepository) : ICommandHandler<AttachSpoolRfidCommand, Spool>
{
    public async Task<Spool> HandleAsync(AttachSpoolRfidCommand command, CancellationToken cancellationToken)
    {
        var spool = await spoolRepository.GetByIdAsync(command.SpoolId, cancellationToken)
            ?? throw new SpoolNotFoundException(command.SpoolId);
        var rfidId = command.RfidId.Trim();
        if ((await spoolRepository.FindByRfidIdsAsync([rfidId], cancellationToken)).Any(s => s.Id != spool.Id))
            throw new SpoolRfidConflictException("RFID spool already registered.");

        spool.AttachRfid(rfidId);
        await spoolRepository.SaveChangesAsync(cancellationToken);
        return spool;
    }
}
