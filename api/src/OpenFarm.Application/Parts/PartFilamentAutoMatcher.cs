using OpenFarm.Application.Production;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>One catalog colour as the matcher sees it.</summary>
public sealed record CatalogColour(Guid VariantId, string VendorName, string MaterialType, string ColorHex);

/// <summary>
/// Pre-selects the catalog colour a sliced file's filament most likely is, so the operator confirms instead of picking. A
/// requirement matches when exactly one colour of the same material (and the same vendor, when the file names one) is clearly
/// closest in colour — the same rule <see cref="ToolTrayMatcher"/> uses for trays. Anything less clear stays unset, which means
/// "use the file's material and colour"; nothing is guessed between near ties.
/// </summary>
public static class PartFilamentAutoMatcher
{
    public static IReadOnlyList<PreviewFilamentRequirement> Apply(
        IReadOnlyList<PreviewFilamentRequirement> requirements, IReadOnlyList<CatalogColour> catalog)
    {
        return requirements.Select(requirement =>
        {
            if (requirement.NarrowFilamentVariantId is not null || requirement.NarrowFilamentProfileId is not null
                || requirement.MaterialType is null) return requirement;

            var trays = catalog
                .Where(c => string.Equals(c.MaterialType, requirement.MaterialType, StringComparison.OrdinalIgnoreCase)
                    && (requirement.DetectedSubBrand is null
                        || string.Equals(c.VendorName, requirement.DetectedSubBrand, StringComparison.OrdinalIgnoreCase)))
                .Select(c => new LoadedTray<Guid>(c.VariantId, c.MaterialType, c.ColorHex))
                .ToList();
            var matched = ToolTrayMatcher.Match([(0, requirement.MaterialType, requirement.DetectedColorHex)], trays, []);
            return matched.TryGetValue(0, out var variantId) ? requirement with { NarrowFilamentVariantId = variantId } : requirement;
        }).ToList();
    }
}
