using OpenFarm.Application.Common;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

public sealed class EditNonPrintedComponentCommandHandler(INonPrintedComponentRepository repository)
    : ICommandHandler<EditNonPrintedComponentCommand, NonPrintedComponent>
{
    public async Task<NonPrintedComponent> HandleAsync(EditNonPrintedComponentCommand command, CancellationToken cancellationToken)
    {
        var component = await repository.GetByIdAsync(command.NonPrintedComponentId, cancellationToken)
            ?? throw new NonPrintedComponentNotFoundException(command.NonPrintedComponentId);

        if (!component.IsLegacyCompatible)
            throw new ArgumentException("Use the cost catalog to edit measured or package-priced items.");
        component.Edit(command.Name, command.UnitCost);
        await repository.SaveChangesAsync(cancellationToken);

        return component;
    }
}
