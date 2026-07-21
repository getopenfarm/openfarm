using OpenFarm.Application.Common;
using OpenFarm.Application.Organizations;

namespace OpenFarm.Application.Parts;

public sealed class GetSlicingDefaultsQueryHandler(IOrganizationRepository organizationRepository)
    : IQueryHandler<GetSlicingDefaultsQuery, SlicingDefaults>
{
    public async Task<SlicingDefaults> HandleAsync(GetSlicingDefaultsQuery query, CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetAsync(cancellationToken);
        return new SlicingDefaults(organization.DefaultSlicerEngine.ToString());
    }
}
