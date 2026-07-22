using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

/// <summary>
/// FR-018/FR-019 (research.md §2): when a part's filament requirement isn't narrowed to a specific
/// profile, slicing auto-resolves a deterministic "representative" System filament profile for the
/// requirement's material type — never an organization-level default (that concept is removed
/// entirely, FR-017). Deterministic and stable across ingestion re-runs by construction: the
/// tie-break key is the vendor's own stable <c>ExternalId</c> (<c>setting_id</c>), not the
/// human-readable <c>Name</c>, so a rename between slicer versions can't flip the choice.
/// </summary>
public sealed class RepresentativeFilamentResolver(IFilamentProfileRepository filamentProfileRepository)
{
    public async Task<FilamentProfile> ResolveAsync(string materialType, CancellationToken cancellationToken)
    {
        var candidates = await filamentProfileRepository.ListAsync(cancellationToken);

        var representative = candidates
            .Where(f => f.Origin == ProfileOrigin.System
                && f.IsSelectable
                && f.IsCurrentlyOffered
                && f.MaterialType == materialType)
            .OrderBy(f => f.VendorName, StringComparer.Ordinal)
            .ThenBy(f => f.ExternalId, StringComparer.Ordinal)
            .FirstOrDefault();

        return representative
            ?? throw new SlicingProfileUnconfiguredException($"No System filament profile is available for material '{materialType}'.");
    }
}
