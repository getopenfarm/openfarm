using OpenFarm.Application.Common;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

/// <summary>Throws <see cref="AssemblyMustHaveAtLeastOneLineException"/> if this is the assembly's last line (FR-002) — delete the assembly itself instead.</summary>
public sealed class RemoveBomLineCommandHandler(IAssemblyRepository assemblyRepository) : ICommandHandler<RemoveBomLineCommand, bool>
{
    public async Task<bool> HandleAsync(RemoveBomLineCommand command, CancellationToken cancellationToken)
    {
        var assembly = await assemblyRepository.GetByIdAsync(command.AssemblyId, cancellationToken)
            ?? throw new AssemblyNotFoundException(command.AssemblyId);

        assembly.RemoveLine(command.LineId);
        await assemblyRepository.SaveChangesAsync(cancellationToken);

        return true;
    }
}
