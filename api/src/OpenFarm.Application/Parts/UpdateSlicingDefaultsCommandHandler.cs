using OpenFarm.Application.Common;
using OpenFarm.Application.Settings;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Application.Parts;

public sealed class UpdateSlicingDefaultsCommandHandler(
    IFarmSettingsRepository settingsRepository,
    ResolveSlicerSelection resolveSlicerSelection,
    IAvailableSlicerCatalog catalog,
    IHostPlatformProvider hostPlatform) : ICommandHandler<UpdateSlicingDefaultsCommand, SlicingDefaults>
{
    public async Task<SlicingDefaults> HandleAsync(UpdateSlicingDefaultsCommand command, CancellationToken cancellationToken)
    {
        // Rejects an unavailable pair before it's ever persisted (FR-007) — the single gate shared
        // with the slicing pipeline's own resolution.
        var validated = resolveSlicerSelection.EnsureAvailable(new SlicerRef(command.Engine, command.Version));

        var settings = await settingsRepository.GetAsync(cancellationToken);
        settings.SetDefaultSlicer(validated);
        await settingsRepository.SaveChangesAsync(cancellationToken);

        var available = AvailableSlicerListing.Build(catalog, hostPlatform.Current);
        return new SlicingDefaults(new EffectiveSlicerDefaultSummary(validated.Engine, validated.Version, IsExplicit: true), available);
    }

}
