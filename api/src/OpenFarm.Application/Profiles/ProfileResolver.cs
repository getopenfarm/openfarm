using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

/// <summary>
/// Resolves a preset the operator selected to the same preset in the catalog of the slicer that will run (the pin:
/// engine + exact version), so "0.20mm Standard" is sliced with Orca's copy under Orca and with Bambu Studio's under
/// Bambu Studio. Fail-closed: when the pinned slicer's own catalog has no such preset there is no answer — never
/// another engine's, another version's or a similarly named preset ("a stored choice that quietly slices with other
/// data" is what pinning a part exists to prevent).
/// <list type="bullet">
/// <item>A System preset already in the pinned catalog is itself (<see cref="ProfileResolutionOutcome.Exact"/>) if it is still offered.</item>
/// <item>Same engine, other version: found by the vendor's <c>setting_id</c> (a rename is tolerated), by vendor + name when the preset has none.</item>
/// <item>Other engine: only the exact twin — same <c>setting_id</c>, vendor and name. Ids are authoritative across engines for BBL presets only, and a rename leaves no trail across engines.</item>
/// <item>A User preset is allowed only in the pinned engine's own format; its System ancestors follow the pin (<see cref="MapAncestorAsync(MachineProfile, SlicerRef, CancellationToken)"/>).</item>
/// <item>A tie at the best match is <see cref="ProfileResolutionOutcome.Ambiguous"/>, never "first wins". A pinned catalog that was never ingested is <see cref="ProfileResolutionOutcome.CatalogNotLoaded"/>: no data, no guess.</item>
/// </list>
/// </summary>
public sealed class ProfileResolver(
    IMachineProfileRepository machines, IProcessProfileRepository processes, IFilamentProfileRepository filaments)
{
    public Task<ProfileResolution<MachineProfile>> ResolveAsync(MachineProfile selected, SlicerRef pin, CancellationToken cancellationToken) =>
        ResolveCoreAsync(selected, pin, ancestor: false, "Machine profile", machines.FindInCatalogByIdentityAsync, cancellationToken);

    public Task<ProfileResolution<ProcessProfile>> ResolveAsync(ProcessProfile selected, SlicerRef pin, CancellationToken cancellationToken) =>
        ResolveCoreAsync(selected, pin, ancestor: false, "Process profile", processes.FindInCatalogByIdentityAsync, cancellationToken);

    public Task<ProfileResolution<FilamentProfile>> ResolveAsync(FilamentProfile selected, SlicerRef pin, CancellationToken cancellationToken) =>
        ResolveCoreAsync(selected, pin, ancestor: false, "Filament profile", filaments.FindInCatalogByIdentityAsync, cancellationToken);

    /// <summary>An ancestor in a preset's inheritance chain: only moves between versions of its own engine, and need not be selectable.</summary>
    /// <exception cref="SlicingProfileUnconfiguredException">The ancestor has no counterpart in the pinned catalog.</exception>
    public async Task<MachineProfile> MapAncestorAsync(MachineProfile ancestor, SlicerRef pin, CancellationToken cancellationToken) =>
        (await ResolveCoreAsync(ancestor, pin, ancestor: true, "Machine profile", machines.FindInCatalogByIdentityAsync, cancellationToken)).Require();

    /// <inheritdoc cref="MapAncestorAsync(MachineProfile, SlicerRef, CancellationToken)"/>
    public async Task<ProcessProfile> MapAncestorAsync(ProcessProfile ancestor, SlicerRef pin, CancellationToken cancellationToken) =>
        (await ResolveCoreAsync(ancestor, pin, ancestor: true, "Process profile", processes.FindInCatalogByIdentityAsync, cancellationToken)).Require();

    /// <inheritdoc cref="MapAncestorAsync(MachineProfile, SlicerRef, CancellationToken)"/>
    public async Task<FilamentProfile> MapAncestorAsync(FilamentProfile ancestor, SlicerRef pin, CancellationToken cancellationToken) =>
        (await ResolveCoreAsync(ancestor, pin, ancestor: true, "Filament profile", filaments.FindInCatalogByIdentityAsync, cancellationToken)).Require();

    private async Task<ProfileResolution<TRow>> ResolveCoreAsync<TRow>(
        TRow row, SlicerRef pin, bool ancestor, string kind,
        Func<ProfileSourceFormat, string, string?, string?, string, CancellationToken, Task<IReadOnlyList<TRow>>> findInCatalog,
        CancellationToken cancellationToken)
        where TRow : class, ICatalogProfile
    {
        var pinned = $"{Label(pin.Engine)} {pin.Version}";
        ProfileResolution<TRow> Blocked(ProfileResolutionOutcome outcome, string message) => new(outcome, row, null, message);
        ProfileResolution<TRow> Usable(ProfileResolutionOutcome outcome, TRow resolved) => new(outcome, row, resolved, null);

        if (ProfileEngineCompatibility.NativeFormat(pin.Engine) is not { } format)
        {
            return Blocked(ProfileResolutionOutcome.EngineWithoutProfiles,
                $"{Label(pin.Engine)} has no profiles. Pin the part to OrcaSlicer or Bambu Studio.");
        }

        var sameEngine = row.SourceFormat == format;
        if (!sameEngine && (ancestor || row.Origin == ProfileOrigin.User))
        {
            var rowEngine = Label(ProfileEngineCompatibility.NativeEngine(row.SourceFormat));
            return Blocked(ProfileResolutionOutcome.UnsupportedOrigin,
                $"{kind} '{row.Name}' is for {rowEngine}. {pinned} uses its own profiles only. Select another profile, or pin the part to {rowEngine}.");
        }

        if (row.Origin == ProfileOrigin.User)
        {
            return Usable(ProfileResolutionOutcome.UserNative, row);
        }

        if (row.SourceVersion is null)
        {
            return Blocked(ProfileResolutionOutcome.InvalidRow,
                $"{kind} '{row.Name}' has no slicer version. Run ingest-system-profiles, then select another profile.");
        }

        var offered = ancestor || (row.IsCurrentlyOffered && row.IsSelectable);
        if (sameEngine && string.Equals(row.SourceVersion, pin.Version, StringComparison.Ordinal))
        {
            return offered
                ? Usable(ProfileResolutionOutcome.Exact, row)
                : Blocked(ProfileResolutionOutcome.NotOffered, $"{kind} '{row.Name}' was removed from {pinned}. Select another profile.");
        }

        var ingested = await machines.ListIngestedVersionsAsync(format, cancellationToken);
        if (!ingested.Contains(pin.Version, StringComparer.Ordinal))
        {
            return Blocked(ProfileResolutionOutcome.CatalogNotLoaded,
                $"{pinned} profiles are not loaded. Restart the app or run ingest-system-profiles.");
        }

        var candidates = await findInCatalog(format, pin.Version, row.ExternalId, row.VendorName, row.Name, cancellationToken);
        var matches = candidates
            .Select(candidate => (Candidate: candidate, Rank: Rank(row, candidate, sameEngine)))
            .Where(match => match.Rank >= 0)
            .ToList();
        if (matches.Count == 0)
        {
            return Blocked(ProfileResolutionOutcome.Missing,
                $"{kind} '{row.Name}' is not in {pinned}. Select another profile, or pin the part to another slicer.");
        }

        var best = matches.Min(match => match.Rank);
        var top = matches.Where(match => match.Rank == best).ToList();
        if (top.Count > 1)
        {
            return Blocked(ProfileResolutionOutcome.Ambiguous, $"{kind} '{row.Name}' matches several profiles in {pinned}. Select another profile.");
        }

        var twin = top[0].Candidate;
        if (!ancestor && !(twin.IsCurrentlyOffered && twin.IsSelectable))
        {
            return Blocked(ProfileResolutionOutcome.NotOffered, $"{kind} '{twin.Name}' was removed from {pinned}. Select another profile.");
        }

        return Usable(sameEngine ? ProfileResolutionOutcome.MappedVersion : ProfileResolutionOutcome.MappedEngine, twin);
    }

    // Lower is better, -1 is no match. A preset with a setting_id must keep it and its vendor; the same id and vendor
    // under another name is a rename — believed within one engine, not across engines. Without an id only vendor + name
    // identify the preset, and only within one engine.
    private static int Rank(ICatalogProfile row, ICatalogProfile candidate, bool sameEngine)
    {
        var sameVendor = string.Equals(candidate.VendorName, row.VendorName, StringComparison.Ordinal);
        var sameName = string.Equals(candidate.Name, row.Name, StringComparison.Ordinal);
        if (row.ExternalId is null)
        {
            return sameEngine && sameVendor && sameName ? 2 : -1;
        }

        if (!sameVendor || !string.Equals(candidate.ExternalId, row.ExternalId, StringComparison.Ordinal))
        {
            return -1;
        }

        return sameName ? 0 : sameEngine ? 1 : -1;
    }

    private static string Label(SlicerEngine engine) => engine == SlicerEngine.BambuStudio ? "Bambu Studio" : engine.ToString();
}
