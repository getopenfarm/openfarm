using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed class DeletePartCommandHandler(
    IPartRepository partRepository, IPartFileStorage storage, IPartEventPublisher eventPublisher, IAssemblyRepository assemblyRepository)
    : ICommandHandler<DeletePartCommand, bool>
{
    public async Task<bool> HandleAsync(DeletePartCommand command, CancellationToken cancellationToken)
    {
        var part = await partRepository.GetByIdAsync(command.PartId, cancellationToken)
            ?? throw new PartNotFoundException(command.PartId);

        // FR-011 (013-assembly-bom): narrows this feature's own "delete cascades to the part's own
        // data" rule — a Part still targeted by a BOM line cannot be deleted out from under it.
        var referencingNames = await assemblyRepository.GetReferencingAssemblyNamesAsync(
            BomLineTargetKind.Part, part.Id, cancellationToken);
        if (referencingNames.Count > 0)
        {
            throw new PartReferencedByAssemblyException(part.Id, referencingNames);
        }

        await partRepository.RemoveAsync(part, cancellationToken);
        await storage.DeleteAsync(part.StoragePath, cancellationToken);
        await eventPublisher.PartDeletedAsync(part.OrganizationId, part.Id, cancellationToken);

        return true;
    }
}
