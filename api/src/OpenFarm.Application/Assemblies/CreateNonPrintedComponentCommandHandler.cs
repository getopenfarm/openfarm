using OpenFarm.Application.Common;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Organizations;

namespace OpenFarm.Application.Assemblies;

public sealed class CreateNonPrintedComponentCommandHandler(INonPrintedComponentRepository repository)
    : ICommandHandler<CreateNonPrintedComponentCommand, NonPrintedComponent>
{
    public async Task<NonPrintedComponent> HandleAsync(CreateNonPrintedComponentCommand command, CancellationToken cancellationToken)
    {
        var component = NonPrintedComponent.Create(Organization.WellKnownId, command.Name, command.Unit, command.UnitCost);
        await repository.AddAsync(component, cancellationToken);

        return component;
    }
}
