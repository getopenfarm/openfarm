using OpenFarm.Application.Common;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed class DeletePartCommandHandler(
    IPartRepository partRepository, IPartFileStorage storage, IPartEventPublisher eventPublisher)
    : ICommandHandler<DeletePartCommand, bool>
{
    public async Task<bool> HandleAsync(DeletePartCommand command, CancellationToken cancellationToken)
    {
        var part = await partRepository.GetByIdAsync(command.PartId, cancellationToken)
            ?? throw new PartNotFoundException(command.PartId);

        await partRepository.RemoveAsync(part, cancellationToken);
        await storage.DeleteAsync(part.StoragePath, cancellationToken);
        await eventPublisher.PartDeletedAsync(part.OrganizationId, part.Id, cancellationToken);

        return true;
    }
}
