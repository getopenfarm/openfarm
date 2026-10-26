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

    /// <summary>
    /// Each plate as indices into <paramref name="requirements"/> (grouped from the same slots): the requirements its own
    /// slots name, or every requirement when it names none (no slots of its own, or the STL "any material" wildcard).
    /// </summary>
    public static IReadOnlyList<PreviewPlate> Plates(IReadOnlyList<PreviewFilamentRequirement> requirements, IReadOnlyList<FilePlate> plates) =>
        plates.Select(plate =>
        {
            var own = requirements.Where(r => plate.FilamentSlots.Any(s =>
                s.MaterialType == r.MaterialType && s.DetectedColorHex == r.DetectedColorHex)).Select(r => r.RequirementIndex).ToList();
            return new PreviewPlate(plate.Number, plate.Name, own.Count > 0 ? own : requirements.Select(r => r.RequirementIndex).ToList(),
                plate.EstimatedPrintTimeMinutes, plate.EstimatedFilamentWeightGrams, plate.FilamentGrams);
        }).ToList();
}
