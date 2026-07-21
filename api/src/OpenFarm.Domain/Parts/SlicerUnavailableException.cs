namespace OpenFarm.Domain.Parts;

/// <summary>
/// A requested/default <see cref="SlicerRef"/> is not present in the bundle for the host platform
/// (FR-006/FR-007/FR-010/FR-021) — maps to <c>422</c> ProblemDetails. The message names the engine
/// and version so a self-hosting operator can act on it (FR-021).
/// </summary>
public sealed class SlicerUnavailableException : Exception
{
    public SlicerUnavailableException(SlicerEngine engine, string version, SlicerPlatform platform)
        : base($"{engine} {version} is not bundled for this platform ({platform}).")
    {
    }

    public SlicerUnavailableException(string message)
        : base(message)
    {
    }
}
