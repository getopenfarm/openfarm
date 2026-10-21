using OpenFarm.Application.Parts;
using OpenFarm.Application.Settings;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

/// <summary>
/// Picks, per source format, the one System catalog version that pickers and name lookups read: the farm's
/// default slicer version when its engine matches the format, else the newest bundled version of that engine —
/// but only if rows were ingested for it — else the newest ingested version; a format with nothing ingested gets no
/// entry (no catalog loaded). The presence rule keeps pickers populated when a slicer is bundled but not yet ingested.
/// </summary>
public sealed class ProfileCatalogVersions(IFarmSettingsRepository farmSettings, ResolveSlicerSelection slicerSelection, IMachineProfileRepository machines)
{
    private static readonly SlicerVersionComparer VersionComparer = new();

    public async Task<ProfileCatalogScope> GetScopeAsync(CancellationToken cancellationToken)
    {
        var settings = await farmSettings.GetAsync(cancellationToken);
        var effectiveDefault = slicerSelection.ResolveEffectiveDefault(settings.DefaultSlicer).Default;

        var versions = new Dictionary<ProfileSourceFormat, string>();
        foreach (var format in Enum.GetValues<ProfileSourceFormat>())
        {
            var engine = ProfileEngineCompatibility.NativeEngine(format);
            var preferred = effectiveDefault is { } slicer && slicer.Engine == engine
                ? slicer.Version
                : slicerSelection.NewestAvailable(engine)?.Version;

            var ingested = await machines.ListIngestedVersionsAsync(format, cancellationToken);
            var version = preferred is not null && ingested.Contains(preferred, StringComparer.Ordinal)
                ? preferred
                : ingested.OrderByDescending(candidate => candidate, VersionComparer).FirstOrDefault();
            if (version is not null)
            {
                versions[format] = version;
            }
        }

        return new ProfileCatalogScope(versions)
        {
            NativeFormat = effectiveDefault is { } defaultSlicer ? ProfileEngineCompatibility.NativeFormat(defaultSlicer.Engine) : null,
        };
    }
}
