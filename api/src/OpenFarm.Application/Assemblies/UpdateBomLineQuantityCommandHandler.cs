using OpenFarm.Application.Common;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

public sealed class UpdateBomLineQuantityCommandHandler(IAssemblyRepository assemblyRepository)
    : ICommandHandler<UpdateBomLineQuantityCommand, BomLine>
{
    public async Task<BomLine> HandleAsync(UpdateBomLineQuantityCommand command, CancellationToken cancellationToken)
    {
        var assembly = await assemblyRepository.GetByIdAsync(command.AssemblyId, cancellationToken)
            ?? throw new AssemblyNotFoundException(command.AssemblyId);

        assembly.UpdateLineQuantity(command.LineId, command.Quantity);
        await assemblyRepository.SaveChangesAsync(cancellationToken);

        return assembly.Lines.Single(l => l.Id == command.LineId);
    }
}
