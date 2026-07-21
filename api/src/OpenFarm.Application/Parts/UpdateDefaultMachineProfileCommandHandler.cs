using OpenFarm.Application.Common;
using OpenFarm.Application.Organizations;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Application.Parts;

public sealed class UpdateDefaultMachineProfileCommandHandler(
    IOrganizationRepository organizationRepository, IMachineProfileRepository machineProfileRepository)
    : ICommandHandler<UpdateDefaultMachineProfileCommand, DefaultMachineProfileResult>
{
    public async Task<DefaultMachineProfileResult> HandleAsync(UpdateDefaultMachineProfileCommand command, CancellationToken cancellationToken)
    {
        var machineProfile = await machineProfileRepository.GetByIdAsync(command.MachineProfileId, cancellationToken)
            ?? throw new MachineProfileNotFoundException(command.MachineProfileId);

        var organization = await organizationRepository.GetAsync(cancellationToken);
        organization.SetDefaultMachineProfile(machineProfile.Id);
        await organizationRepository.SaveChangesAsync(cancellationToken);

        return new DefaultMachineProfileResult(machineProfile.Id, machineProfile.Name, machineProfile.DefaultProcessProfileId);
    }
}
