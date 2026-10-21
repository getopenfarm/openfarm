using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

/// <summary>
/// How a selected preset relates to the slicer pin. The first four values are usable; every other value blocks
/// production: nothing is generated or sent, and no other engine, version or preset is substituted.
/// </summary>
public enum ProfileResolutionOutcome
{
    /// <summary>The selected row already is the pinned slicer's own preset.</summary>
    Exact,

    /// <summary>The same preset in another version of the pinned engine, found by its <c>setting_id</c> (a rename is tolerated).</summary>
    MappedVersion,

    /// <summary>The pinned engine's exact twin (same <c>setting_id</c>, vendor and name) of the other engine's preset.</summary>
    MappedEngine,

    /// <summary>An imported preset in the pinned engine's own format; its System ancestors follow the pin.</summary>
    UserNative,

    NoPin,
    SlicerUnavailable,
    EngineWithoutProfiles,
    UnsupportedOrigin,
    NotOffered,
    Missing,
    Ambiguous,
    CatalogNotLoaded,
    InvalidRow,

    /// <summary>The resolved presets do not fit each other or the printer (checked after mapping).</summary>
    Incompatible,
}

public static class ProfileResolutionOutcomes
{
    public static bool IsUsable(this ProfileResolutionOutcome outcome) =>
        outcome is ProfileResolutionOutcome.Exact or ProfileResolutionOutcome.MappedVersion
            or ProfileResolutionOutcome.MappedEngine or ProfileResolutionOutcome.UserNative;
}

/// <summary>The outcome for one selected preset: the row to slice with, or why there is none (<see cref="Resolved"/> is then <c>null</c>).</summary>
public sealed record ProfileResolution<TRow>(ProfileResolutionOutcome Outcome, TRow Selected, TRow? Resolved, string? Message)
    where TRow : class, ICatalogProfile
{
    public TRow Require() =>
        Resolved ?? throw new SlicingProfileUnconfiguredException(Message ?? "The profile cannot be used.", Outcome.ToString());
}

/// <summary>What one preset was selected as and what it resolved to — kept on the resolved set, in the configuration stamp and in logs.</summary>
public sealed record ResolvedPreset(
    ProfileKind Kind, Guid SelectedId, string SelectedName, Guid ResolvedId, string ResolvedName,
    ProfileSourceFormat Format, string? SourceVersion, string? SettingId, string? VendorName, ProfileResolutionOutcome Outcome)
{
    /// <summary>The line logs and diagnostics show: the preset used, how it was reached, its setting id and catalog version.</summary>
    public override string ToString() => $"{ResolvedName} ({Outcome}, id {SettingId ?? "none"}, v{SourceVersion})";

    public static ResolvedPreset From<TRow>(ProfileKind kind, ProfileResolution<TRow> resolution) where TRow : class, ICatalogProfile
    {
        var resolved = resolution.Resolved ?? throw new InvalidOperationException("A blocked resolution has no resolved preset.");
        return new(kind, resolution.Selected.Id, resolution.Selected.Name, resolved.Id, resolved.Name,
            resolved.SourceFormat, resolved.SourceVersion, resolved.ExternalId, resolved.VendorName, resolution.Outcome);
    }
}

/// <summary>One reason a set of presets cannot be used with a pin. <see cref="Kind"/> is <c>null</c> for a problem of the set as a whole (a broken inheritance chain, a missing bed temperature).</summary>
public sealed record ProfileIssue(ProfileKind? Kind, ProfileResolutionOutcome Code, string Name, string Message);

/// <summary>Every reason at once, so the operator can fix them in one go. Still a <see cref="SlicingProfileUnconfiguredException"/> to every existing handler.</summary>
public sealed class ProfileSetBlockedException(IReadOnlyList<ProfileIssue> issues)
    : SlicingProfileUnconfiguredException(string.Join(" ", issues.Select(issue => issue.Message).Distinct()), issues[0].Code.ToString())
{
    public IReadOnlyList<ProfileIssue> Issues { get; } = issues;
}
