using OpenFarm.Application.Common;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

/// <summary>Rename/redescribe only — line changes go through the line endpoints (contracts/assembly-bom-api.md).</summary>
public sealed class EditAssemblyCommandHandler(IAssemblyRepository assemblyRepository) : ICommandHandler<EditAssemblyCommand, Assembly>
{
    public async Task<Assembly> HandleAsync(EditAssemblyCommand command, CancellationToken cancellationToken)
    {
        var assembly = await assemblyRepository.GetByIdAsync(command.AssemblyId, cancellationToken)
            ?? throw new AssemblyNotFoundException(command.AssemblyId);

        assembly.Rename(command.Name, command.Description);
        await assemblyRepository.SaveChangesAsync(cancellationToken);

        return assembly;
    }
}
