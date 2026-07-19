using OpenFarm.Application.Common;
using OpenFarm.Application.Organizations;

namespace OpenFarm.Application.Parts;

public sealed class UpdateSlicingDefaultsCommandHandler(IOrganizationRepository organizationRepository)
    : ICommandHandler<UpdateSlicingDefaultsCommand, SlicingDefaults>
{
    public async Task<SlicingDefaults> HandleAsync(UpdateSlicingDefaultsCommand command, CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetAsync(cancellationToken);
        organization.SetDefaultSlicerEngine(command.DefaultSlicerEngine);
        await organizationRepository.SaveChangesAsync(cancellationToken);

        return new SlicingDefaults(organization.DefaultSlicerEngine.ToString());
    }
}
