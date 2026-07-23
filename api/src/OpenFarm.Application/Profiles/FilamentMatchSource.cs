namespace OpenFarm.Application.Profiles;

/// <summary>
/// Which tier of <see cref="RepresentativeFilamentResolver"/>'s tiered search produced a match
/// (research.md R2/R6, data-model.md). Not persisted this iteration — kept as a plain internal
/// classification so a future audit-trail/debugging feature can consume it without a resolver rewrite.
/// </summary>
public enum FilamentMatchSource
{
    /// <summary>A spool currently <c>Using</c> a fleet printer's feed slot (research.md R2 steps 1-2).</summary>
    FleetLoaded,

    /// <summary>A spool <c>InStorage</c> (research.md R2 step 3).</summary>
    FilamentLibrary,

    /// <summary>A generic System filament profile with no owned spool behind it (research.md R2 step 4).</summary>
    SystemCatalog,
}
