namespace OpenFarm.Application.Parts;

/// <summary>
/// Application-facing seam over <c>Infrastructure.Parts.SlicerOptions</c> (a fixed, hardcoded,
/// never operator-configurable constant, research.md §7a) — keeps <c>AddPartCommandHandler</c> from
/// depending on Infrastructure directly.
/// </summary>
public interface ISlicerDefaultsProvider
{
    /// <summary>The fixed fallback material type an STL slices against — the only remaining "any material" case (research.md §7a), engine-independent.</summary>
    string GetFallbackMaterialType();
}
