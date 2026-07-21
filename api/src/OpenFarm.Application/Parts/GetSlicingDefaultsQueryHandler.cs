using OpenFarm.Application.Common;
using OpenFarm.Application.Organizations;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed class GetSlicingDefaultsQueryHandler(
    IOrganizationRepository organizationRepository,
    IAvailableSlicerCatalog catalog,
    IHostPlatformProvider hostPlatform,
    ResolveSlicerSelection resolveSlicerSelection) : IQueryHandler<GetSlicingDefaultsQuery, SlicingDefaults>
{
    public async Task<SlicingDefaults> HandleAsync(GetSlicingDefaultsQuery query, CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetAsync(cancellationToken);
        var effective = resolveSlicerSelection.ResolveEffectiveDefault(organization.DefaultSlicer);

        var available = AvailableSlicerListing.Build(catalog, hostPlatform.Current);

        var defaultSummary = effective.Default is { } def
            ? new EffectiveSlicerDefaultSummary(def.Engine, def.Version, effective.IsExplicit)
            : null;

        return new SlicingDefaults(defaultSummary, available);
    }
}
