using OpenFarm.Application.Common;
using OpenFarm.Application.Settings;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Application.Parts;

public sealed class UpdateSlicingDefaultsCommandHandler(
    IFarmSettingsRepository settingsRepository,
    ResolveSlicerSelection resolveSlicerSelection,
    IAvailableSlicerCatalog catalog,
    IHostPlatformProvider hostPlatform, ResolveEstimationProfiles? estimationProfiles = null) : ICommandHandler<UpdateSlicingDefaultsCommand, SlicingDefaults>
{
    public async Task<SlicingDefaults> HandleAsync(UpdateSlicingDefaultsCommand command, CancellationToken cancellationToken)
    {
        // Rejects an unavailable pair before it's ever persisted (FR-007) — the single gate shared
        // with the slicing pipeline's own resolution.
        var validated = resolveSlicerSelection.EnsureAvailable(new SlicerRef(command.Engine, command.Version));

        var settings = await settingsRepository.GetAsync(cancellationToken);
        var profiles = command.UpdateEstimationProfiles ? command.EstimationProfiles : GetSelection(settings);
        if (profiles is not null)
        {
            if (estimationProfiles is null) throw new SlicingProfileUnconfiguredException("Estimation profile resolution is unavailable.");
            _ = await estimationProfiles.ResolveAsync(profiles, validated, cancellationToken);
        }
        if (command.UpdateEstimationProfiles)
            settings.SetEstimationProfiles(profiles?.MachineProfileId, profiles?.ProcessProfileId, profiles?.FilamentProfileId, profiles?.BuildPlateType);
        settings.SetDefaultSlicer(validated);
        await settingsRepository.SaveChangesAsync(cancellationToken);

        var available = AvailableSlicerListing.Build(catalog, hostPlatform.Current);
        return new SlicingDefaults(new EffectiveSlicerDefaultSummary(validated.Engine, validated.Version, IsExplicit: true), available, GetSelection(settings));
    }
    public static EstimationProfileSelection? GetSelection(FarmSettings settings) =>
        settings.EstimationMachineProfileId is { } m && settings.EstimationProcessProfileId is { } p
            && settings.EstimationFilamentProfileId is { } f && settings.EstimationBuildPlateType is { } b
            ? new(m, p, f, b) : null;
}
