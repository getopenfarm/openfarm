using OpenFarm.Application.Common;
using OpenFarm.Application.Organizations;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Parts;

public sealed class UpdateOrganizationFilamentDefaultCommandHandler(
    IOrganizationRepository organizationRepository,
    IFilamentProfileRepository filamentProfileRepository,
    IOrganizationFilamentDefaultRepository filamentDefaultRepository)
    : ICommandHandler<UpdateOrganizationFilamentDefaultCommand, OrganizationFilamentDefaultResult>
{
    public async Task<OrganizationFilamentDefaultResult> HandleAsync(UpdateOrganizationFilamentDefaultCommand command, CancellationToken cancellationToken)
    {
        var filamentProfile = await filamentProfileRepository.GetByIdAsync(command.FilamentProfileId, cancellationToken)
            ?? throw new FilamentProfileNotFoundException(command.FilamentProfileId);

        var existing = await filamentDefaultRepository.FindByMaterialTypeAsync(command.MaterialType, cancellationToken);
        if (existing is not null)
        {
            existing.ReplaceFilamentProfile(filamentProfile.Id);
            await filamentDefaultRepository.SaveChangesAsync(cancellationToken);
            return new OrganizationFilamentDefaultResult(command.MaterialType, filamentProfile.Id);
        }

        var organization = await organizationRepository.GetAsync(cancellationToken);
        var entry = OrganizationFilamentDefault.Create(organization.Id, command.MaterialType, filamentProfile.Id);
        await filamentDefaultRepository.AddAsync(entry, cancellationToken);

        return new OrganizationFilamentDefaultResult(command.MaterialType, filamentProfile.Id);
    }
}
