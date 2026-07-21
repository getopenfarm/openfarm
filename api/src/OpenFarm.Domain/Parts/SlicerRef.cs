namespace OpenFarm.Domain.Parts;

/// <summary>
/// A validated selection identity — the org default, a per-part override, or a resolved slice
/// target (data-model.md "SlicerRef"). <see cref="Version"/> is the exact upstream string, stored
/// and shown verbatim (FR-005). This type alone carries no availability guarantee — only
/// <c>ResolveSlicerSelection</c> checks a <see cref="SlicerRef"/> against the catalog.
/// </summary>
public sealed record SlicerRef
{
    public SlicerRef(SlicerEngine engine, string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Slicer version is required.", nameof(version));
        }

        Engine = engine;
        Version = version;
    }

    public SlicerEngine Engine { get; }

    public string Version { get; }

    public override string ToString() => $"{Engine} {Version}";
}
