using OpenFarm.Application.Common;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

/// <summary>FR-011: blocked (naming every referencing assembly) if this component is still targeted by a BOM line.</summary>
public sealed class DeleteNonPrintedComponentCommandHandler(
    INonPrintedComponentRepository componentRepository, IAssemblyRepository assemblyRepository)
    : ICommandHandler<DeleteNonPrintedComponentCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteNonPrintedComponentCommand command, CancellationToken cancellationToken)
    {
        var component = await componentRepository.GetByIdAsync(command.NonPrintedComponentId, cancellationToken)
            ?? throw new NonPrintedComponentNotFoundException(command.NonPrintedComponentId);

        var referencingNames = await assemblyRepository.GetReferencingAssemblyNamesAsync(
            BomLineTargetKind.NonPrintedComponent, component.Id, cancellationToken);
        if (referencingNames.Count > 0)
        {
            throw new NonPrintedComponentReferencedException(component.Id, referencingNames);
        }

        await componentRepository.RemoveAsync(component, cancellationToken);

        return true;
    }
}
