namespace OpenFarm.Domain.Parts;

/// <summary>
/// The default machine profile, its paired process profile, or a material's default
/// filament profile isn't configured (or one of their raw-data ancestor chains is unresolved) — maps
/// to <c>422</c> ProblemDetails. The message names exactly what's missing so a self-hosting operator
/// can act on it, matching <see cref="SlicerUnavailableException"/>'s treatment.
/// </summary>
public sealed class SlicingProfileUnconfiguredException(string message) : Exception(message)
{
}
