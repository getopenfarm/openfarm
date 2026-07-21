using OpenFarm.Application.Common;
using OpenFarm.Application.Organizations;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed class UpdateSlicingDefaultsCommandHandler(
    IOrganizationRepository organizationRepository,
    ResolveSlicerSelection resolveSlicerSelection,
    IAvailableSlicerCatalog catalog,
    IHostPlatformProvider hostPlatform) : ICommandHandler<UpdateSlicingDefaultsCommand, SlicingDefaults>
{
    public async Task<SlicingDefaults> HandleAsync(UpdateSlicingDefaultsCommand command, CancellationToken cancellationToken)
    {
        // Rejects an unavailable pair before it's ever persisted (FR-007) — the single gate shared
        // with the slicing pipeline's own resolution.
        var validated = resolveSlicerSelection.EnsureAvailable(new SlicerRef(command.Engine, command.Version));

        var organization = await organizationRepository.GetAsync(cancellationToken);
        organization.SetDefaultSlicer(validated);
        await organizationRepository.SaveChangesAsync(cancellationToken);

        var available = AvailableSlicerListing.Build(catalog, hostPlatform.Current);
        return new SlicingDefaults(new EffectiveSlicerDefaultSummary(validated.Engine, validated.Version, IsExplicit: true), available);
    }
}
