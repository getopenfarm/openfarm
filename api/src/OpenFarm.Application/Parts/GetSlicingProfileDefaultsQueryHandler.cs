using OpenFarm.Application.Common;
using OpenFarm.Application.Organizations;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Application.Parts;

// Unlike ResolveSlicingProfileSelection, this tolerates every unconfigured state as a valid response
// (null/empty, not a thrown exception) — an operator needs to see "nothing configured yet" to know
// what to set, not a 422.
public sealed class GetSlicingProfileDefaultsQueryHandler(
    IOrganizationRepository organizationRepository,
    IMachineProfileRepository machineProfileRepository,
    IProcessProfileRepository processProfileRepository,
    IFilamentProfileRepository filamentProfileRepository,
    IOrganizationFilamentDefaultRepository filamentDefaultRepository)
    : IQueryHandler<GetSlicingProfileDefaultsQuery, SlicingProfileDefaults>
{
    public async Task<SlicingProfileDefaults> HandleAsync(GetSlicingProfileDefaultsQuery query, CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetAsync(cancellationToken);

        DefaultMachineProfileInfo? machineInfo = null;
        if (organization.DefaultMachineProfileId is { } machineProfileId)
        {
            var machineProfile = await machineProfileRepository.GetByIdAsync(machineProfileId, cancellationToken);
            if (machineProfile is not null)
            {
                DefaultProcessProfileInfo? processInfo = null;
                if (machineProfile.DefaultProcessProfileId is { } processProfileId)
                {
                    var processProfile = await processProfileRepository.GetByIdAsync(processProfileId, cancellationToken);
                    if (processProfile is not null)
                    {
                        processInfo = new DefaultProcessProfileInfo(processProfile.Id, processProfile.Name);
                    }
                }

                machineInfo = new DefaultMachineProfileInfo(machineProfile.Id, machineProfile.Name, processInfo);
            }
        }

        var filamentDefaultRows = await filamentDefaultRepository.ListAsync(cancellationToken);
        var filamentDefaults = new List<FilamentDefaultInfo>(filamentDefaultRows.Count);
        foreach (var row in filamentDefaultRows)
        {
            var filamentProfile = await filamentProfileRepository.GetByIdAsync(row.FilamentProfileId, cancellationToken);
            if (filamentProfile is not null)
            {
                filamentDefaults.Add(new FilamentDefaultInfo(row.MaterialType, filamentProfile.Id, filamentProfile.Name));
            }
        }

        return new SlicingProfileDefaults(machineInfo, filamentDefaults);
    }
}
