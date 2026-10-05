using OpenFarm.Application.Common;
using OpenFarm.Application.Organizations;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed class UpdateSlicingDefaultsCommandHandler(
    IOrganizationRepository organizationRepository,
    ResolveSlicerSelection resolveSlicerSelection,
    IAvailableSlicerCatalog catalog,
    IHostPlatformProvider hostPlatform, ResolveEstimationProfiles? estimationProfiles = null) : ICommandHandler<UpdateSlicingDefaultsCommand, SlicingDefaults>
{
    public async Task<SlicingDefaults> HandleAsync(UpdateSlicingDefaultsCommand command, CancellationToken cancellationToken)
    {
        // Rejects an unavailable pair before it's ever persisted (FR-007) — the single gate shared
        // with the slicing pipeline's own resolution.
        var validated = resolveSlicerSelection.EnsureAvailable(new SlicerRef(command.Engine, command.Version));

        var organization = await organizationRepository.GetAsync(cancellationToken);
        var profiles = command.UpdateEstimationProfiles ? command.EstimationProfiles : GetSelection(organization);
        if (profiles is not null)
        {
            if (estimationProfiles is null) throw new SlicingProfileUnconfiguredException("Estimation profile resolution is unavailable.");
            _ = await estimationProfiles.ResolveAsync(profiles, command.Engine, cancellationToken);
        }
        if (command.UpdateEstimationProfiles)
            organization.SetEstimationProfiles(profiles?.MachineProfileId, profiles?.ProcessProfileId, profiles?.FilamentProfileId, profiles?.BuildPlateType);
        organization.SetDefaultSlicer(validated);
        await organizationRepository.SaveChangesAsync(cancellationToken);

        var available = AvailableSlicerListing.Build(catalog, hostPlatform.Current);
        return new SlicingDefaults(new EffectiveSlicerDefaultSummary(validated.Engine, validated.Version, IsExplicit: true), available, GetSelection(organization));
    }
    public static EstimationProfileSelection? GetSelection(OpenFarm.Domain.Organizations.Organization organization) =>
        organization.EstimationMachineProfileId is { } m && organization.EstimationProcessProfileId is { } p
            && organization.EstimationFilamentProfileId is { } f && organization.EstimationBuildPlateType is { } b
            ? new(m, p, f, b) : null;
}
