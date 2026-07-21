using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>
/// Groups every detected filament slot across every plate/AMS slot in a file into one
/// <see cref="PreviewFilamentRequirement"/> per distinct <c>(MaterialType, DetectedColorHex)</c> pair,
/// in first-seen order (research.md §7, TDD per §12). Never invoked for the STL "any material"
/// wildcard, which is constructed directly by <c>PartFileProcessor</c> instead.
/// </summary>
public static class FilamentRequirementGrouper
{
    public static IReadOnlyList<PreviewFilamentRequirement> Group(IReadOnlyList<DetectedFilamentSlot> detectedSlots)
    {
        var requirements = new List<PreviewFilamentRequirement>();
        var indexByPair = new Dictionary<(string MaterialType, string? DetectedColorHex), int>();

        foreach (var slot in detectedSlots)
        {
            var key = (slot.MaterialType, slot.DetectedColorHex);
            if (!indexByPair.ContainsKey(key))
            {
                indexByPair[key] = requirements.Count;
                requirements.Add(new PreviewFilamentRequirement(requirements.Count, slot.MaterialType, slot.DetectedColorHex, slot.DetectedSubBrand));
            }
        }

        return requirements;
    }
}
