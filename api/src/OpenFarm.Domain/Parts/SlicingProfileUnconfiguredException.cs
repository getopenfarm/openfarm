namespace OpenFarm.Domain.Parts;

/// <summary>
/// The default machine profile, its paired process profile, or a material's default
/// filament profile isn't configured (or one of their raw-data ancestor chains is unresolved) — maps
/// to <c>422</c> ProblemDetails. The message names exactly what's missing so a self-hosting operator
/// can act on it, matching <see cref="SlicerUnavailableException"/>'s treatment.
/// <para>
/// <see cref="Code"/> is a stable, machine-readable reason (a <c>ProfileResolutionOutcome</c> name) when the failure
/// is a profile resolution block, so callers can react without parsing the message; <c>null</c> otherwise.
/// </para>
/// </summary>
public class SlicingProfileUnconfiguredException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}
