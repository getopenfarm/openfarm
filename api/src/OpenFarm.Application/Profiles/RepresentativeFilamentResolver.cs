using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

/// <summary>
/// FR-018/FR-019 (research.md of 009-process-profiles) plus the tiered rewrite from
/// 010-filament-matching (research.md R2/R3): when a part's filament requirement isn't narrowed to a
/// specific profile, slicing auto-resolves a deterministic match for the requirement's material type —
/// a spool loaded on the target printer, then loaded anywhere in the fleet, then the filament library
/// (storage), and only then a generic System catalog profile. Never an organization-level default
/// (removed entirely, FR-017). Deterministic and stable across ingestion re-runs by construction: every
/// tier's tie-break is a stable-by-Id ordering (never the human-readable <c>Name</c>), matching the
/// codebase's existing "no meaningful preference, pick deterministically" idiom
/// (<c>ResolveSlicingProfileSelection.ResolveForFleetRepresentativeAsync</c>).
/// </summary>
public sealed class RepresentativeFilamentResolver(
    IFilamentProfileRepository filamentProfileRepository,
    ISpoolRepository spoolRepository,
    IFilamentRepository filamentRepository,
    IFeedUnitRepository feedUnitRepository)
{
    public async Task<FilamentMatchResult> ResolveAsync(Guid? printerId, string materialType, CancellationToken cancellationToken)
    {
        var loadedSpools = await spoolRepository.ListAsync(SpoolState.Using, filamentVariantId: null, cancellationToken);
        var fleetCandidates = await ResolveFleetLoadedCandidatesAsync(loadedSpools, materialType, cancellationToken);

        if (printerId is { } targetPrinterId)
        {
            var targetPrinterMatch = fleetCandidates
                .Where(c => c.PrinterId == targetPrinterId)
                .OrderBy(c => c.Spool.Id)
                .FirstOrDefault();
            if (targetPrinterMatch is { } match)
            {
                return new FilamentMatchResult(match.Profile, FilamentMatchSource.FleetLoaded);
            }
        }

        var fleetMatch = fleetCandidates
            .OrderBy(c => c.PrinterId)
            .ThenBy(c => c.Spool.Id)
            .FirstOrDefault();
        if (fleetMatch is { } fleetPick)
        {
            return new FilamentMatchResult(fleetPick.Profile, FilamentMatchSource.FleetLoaded);
        }

        var storedSpools = await spoolRepository.ListAsync(SpoolState.InStorage, filamentVariantId: null, cancellationToken);
        var libraryCandidates = await ResolveLibraryCandidatesAsync(storedSpools, materialType, cancellationToken);
        var libraryMatch = libraryCandidates.OrderBy(c => c.Spool.Id).FirstOrDefault();
        if (libraryMatch is { } libraryPick)
        {
            return new FilamentMatchResult(libraryPick.Profile, FilamentMatchSource.FilamentLibrary);
        }

        var systemCandidates = await filamentProfileRepository.ListAsync(cancellationToken);
        var representative = systemCandidates
            .Where(f => f.Origin == ProfileOrigin.System
                && f.IsSelectable
                && f.IsCurrentlyOffered
                && f.MaterialType == materialType)
            .OrderBy(f => f.VendorName, StringComparer.Ordinal)
            .ThenBy(f => f.ExternalId, StringComparer.Ordinal)
            .FirstOrDefault();

        return representative is not null
            ? new FilamentMatchResult(representative, FilamentMatchSource.SystemCatalog)
            : throw new SlicingProfileUnconfiguredException($"No System filament profile is available for material '{materialType}'.");
    }

    private async Task<List<FleetLoadedCandidate>> ResolveFleetLoadedCandidatesAsync(
        IReadOnlyList<Spool> loadedSpools, string materialType, CancellationToken cancellationToken)
    {
        var candidates = new List<FleetLoadedCandidate>();
        foreach (var spool in loadedSpools)
        {
            if (spool.FeedSlotId is not { } feedSlotId)
            {
                continue;
            }

            var profile = await ResolveEligibleProfileAsync(spool, materialType, cancellationToken);
            if (profile is null)
            {
                continue;
            }

            // research.md R4 — no reachability/liveness filtering; a loaded spool counts regardless of
            // whether its printer is currently online.
            var feedUnit = await feedUnitRepository.GetByFeedSlotIdAsync(feedSlotId, cancellationToken);
            if (feedUnit is null)
            {
                continue;
            }

            candidates.Add(new FleetLoadedCandidate(spool, profile, feedUnit.PrinterId));
        }

        return candidates;
    }

    private async Task<List<LibraryCandidate>> ResolveLibraryCandidatesAsync(
        IReadOnlyList<Spool> storedSpools, string materialType, CancellationToken cancellationToken)
    {
        var candidates = new List<LibraryCandidate>();
        foreach (var spool in storedSpools)
        {
            var profile = await ResolveEligibleProfileAsync(spool, materialType, cancellationToken);
            if (profile is not null)
            {
                candidates.Add(new LibraryCandidate(spool, profile));
            }
        }

        return candidates;
    }

    /// <summary>
    /// Bridges a physical spool to the <see cref="FilamentProfile"/> slicing needs via the
    /// Filament-Tracking-to-Profiles link (research.md R3): eligible only when the spool's
    /// <see cref="Filament.MaterialType"/> matches <paramref name="materialType"/> and its
    /// <see cref="Filament.FilamentProfileId"/> is set and resolves to a real row — "never linked" and
    /// "linked but dangling" are both skipped silently (FR-010), never an error.
    /// </summary>
    private async Task<FilamentProfile?> ResolveEligibleProfileAsync(Spool spool, string materialType, CancellationToken cancellationToken)
    {
        var variant = await filamentRepository.FindVariantByIdAsync(spool.FilamentVariantId, cancellationToken);
        if (variant is null)
        {
            return null;
        }

        var filament = await filamentRepository.GetByIdAsync(variant.FilamentId, cancellationToken);
        if (filament is null || filament.MaterialType != materialType || filament.FilamentProfileId is not { } filamentProfileId)
        {
            return null;
        }

        return await filamentProfileRepository.GetByIdAsync(filamentProfileId, cancellationToken);
    }

    private sealed record FleetLoadedCandidate(Spool Spool, FilamentProfile Profile, Guid PrinterId);

    private sealed record LibraryCandidate(Spool Spool, FilamentProfile Profile);
}
