using OpenFarm.Application.Common;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

/// <summary>FR-011: blocked (naming every referencing assembly) if this assembly is still targeted by a BOM line elsewhere; its own lines cascade at the DB layer.</summary>
public sealed class DeleteAssemblyCommandHandler(IAssemblyRepository assemblyRepository) : ICommandHandler<DeleteAssemblyCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteAssemblyCommand command, CancellationToken cancellationToken)
    {
        var assembly = await assemblyRepository.GetByIdAsync(command.AssemblyId, cancellationToken)
            ?? throw new AssemblyNotFoundException(command.AssemblyId);

        var referencingNames = await assemblyRepository.GetReferencingAssemblyNamesAsync(
            BomLineTargetKind.Assembly, assembly.Id, cancellationToken);
        if (referencingNames.Count > 0)
        {
            throw new AssemblyReferencedException(assembly.Id, referencingNames);
        }

        await assemblyRepository.RemoveAsync(assembly, cancellationToken);

        return true;
    }
}
