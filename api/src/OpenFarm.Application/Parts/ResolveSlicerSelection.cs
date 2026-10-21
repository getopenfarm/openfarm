using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>The farm's effective default — the explicit choice if set and still bundled, else the auto-defaulted fallback (research.md §5).</summary>
public sealed record EffectiveSlicerDefault(SlicerRef? Default, bool IsExplicit);

/// <summary>
/// The single availability+platform validation gate fronting both "set as default" and "slice this
/// part" (research.md §7, FR-006/FR-007/FR-010/FR-011) — centralizing the rule keeps it identical
/// for both call sites (Constitution III).
/// </summary>
public sealed class ResolveSlicerSelection(IAvailableSlicerCatalog catalog, IHostPlatformProvider hostPlatform)
{
    private static readonly SlicerVersionComparer VersionComparer = new();

    // OrcaSlicer preferred, then BambuStudio, then SuperSlicer, when no explicit default resolves (research.md §5).
    private static readonly IReadOnlyList<SlicerEngine> EnginePreferenceOrder =
        [SlicerEngine.OrcaSlicer, SlicerEngine.BambuStudio, SlicerEngine.SuperSlicer];

    /// <summary>
    /// Computes the effective default from the optional explicit choice — never
    /// throws; a fully empty catalog yields <c>Default: null</c> (FR-011), the "no slicer available"
    /// UI state.
    /// </summary>
    public EffectiveSlicerDefault ResolveEffectiveDefault(SlicerRef? farmDefault)
    {
        var platform = hostPlatform.Current;
        var available = catalog.GetAvailable().Where(a => a.Platform == platform).ToList();

        if (farmDefault is not null
            && available.Any(a => a.Engine == farmDefault.Engine && string.Equals(a.Version, farmDefault.Version, StringComparison.Ordinal)))
        {
            return new EffectiveSlicerDefault(farmDefault, IsExplicit: true);
        }

        foreach (var engine in EnginePreferenceOrder)
        {
            var newest = NewestFor(available, engine);
            if (newest is not null)
            {
                return new EffectiveSlicerDefault(newest.ToSlicerRef(), IsExplicit: false);
            }
        }

        return new EffectiveSlicerDefault(null, IsExplicit: false);
    }

    /// <summary>
    /// Resolves a requested selection into a validated, host-available <see cref="SlicerRef"/>:
    /// both engine and version present → that exact pair; engine-only → the newest available
    /// version of that engine; neither → the effective farm default.
    /// </summary>
    /// <exception cref="SlicerUnavailableException">The resolved candidate isn't bundled for the host platform, or nothing is available at all.</exception>
    public SlicerRef Resolve(SlicerEngine? overrideEngine, string? overrideVersion, SlicerRef? farmDefault)
    {
        if (overrideEngine is { } engine && overrideVersion is { } version)
        {
            return EnsureAvailable(new SlicerRef(engine, version));
        }

        if (overrideEngine is { } engineOnly)
        {
            var platform = hostPlatform.Current;
            var newest = NewestFor(catalog.GetAvailable().Where(a => a.Platform == platform).ToList(), engineOnly);
            if (newest is null)
            {
                throw new SlicerUnavailableException($"No {engineOnly} version is available for this platform ({platform}).");
            }

            return newest.ToSlicerRef();
        }

        var effective = ResolveEffectiveDefault(farmDefault);
        if (effective.Default is null)
        {
            throw new SlicerUnavailableException("No slicer is available.");
        }

        return effective.Default;
    }

    /// <summary>
    /// The slicer for a part whose file is replaced or reprocessed: what the operator asked for (engine + version, or an
    /// engine alone for its newest version), else the part's current pin — an existing pin is never moved to the farm
    /// default behind the operator's back. Only a part with no pin yet takes the farm default.
    /// </summary>
    /// <exception cref="SlicerUnavailableException">The chosen pair isn't bundled for the host platform.</exception>
    public SlicerRef ResolveKeepingPin(
        SlicerEngine? requestedEngine, string? requestedVersion, SlicerEngine? currentEngine, string? currentVersion, SlicerRef? farmDefault) =>
        requestedEngine is null && requestedVersion is null && currentEngine is { } engine && !string.IsNullOrWhiteSpace(currentVersion)
            ? EnsureAvailable(new SlicerRef(engine, currentVersion))
            : Resolve(requestedEngine, requestedVersion, farmDefault);

    /// <summary>
    /// The slicer a part was pinned to at upload — production slices with exactly that pair or not at all. Unlike
    /// <see cref="Resolve"/> it never fills a gap from the farm default or "the newest version": a part with no complete
    /// pin has no slicer, and changing the pin is an explicit operator action (replace or reprocess the part).
    /// </summary>
    /// <exception cref="SlicingProfileUnconfiguredException">The part has no complete (engine + version) pin.</exception>
    /// <exception cref="SlicerUnavailableException">The pinned pair isn't bundled for the host platform.</exception>
    public SlicerRef ResolvePinned(SlicerEngine? engine, string? version)
    {
        if (engine is not { } pinnedEngine || string.IsNullOrWhiteSpace(version))
        {
            throw new SlicingProfileUnconfiguredException(
                "The part has no slicer pinned. Reprocess the part to pin a slicer.", nameof(Profiles.ProfileResolutionOutcome.NoPin));
        }

        return EnsureAvailable(new SlicerRef(pinnedEngine, version));
    }

    /// <summary>The newest bundled version of <paramref name="engine"/> for the host platform, or <c>null</c> when none is.</summary>
    public SlicerRef? NewestAvailable(SlicerEngine engine) =>
        NewestFor(catalog.GetAvailable().Where(a => a.Platform == hostPlatform.Current).ToList(), engine)?.ToSlicerRef();

    /// <summary>Validates a fully-specified pair is bundled for the host platform (FR-007) — used directly by "set as default".</summary>
    /// <exception cref="SlicerUnavailableException"><paramref name="requested"/> isn't bundled for the host platform.</exception>
    public SlicerRef EnsureAvailable(SlicerRef requested)
    {
        var platform = hostPlatform.Current;
        var found = catalog.Find(requested, platform);
        if (found is null)
        {
            throw new SlicerUnavailableException(requested.Engine, requested.Version, platform);
        }

        return found.ToSlicerRef();
    }

    private static AvailableSlicer? NewestFor(IReadOnlyList<AvailableSlicer> available, SlicerEngine engine) =>
        available
            .Where(a => a.Engine == engine)
            .OrderByDescending(a => a.Version, VersionComparer)
            .FirstOrDefault();
}
