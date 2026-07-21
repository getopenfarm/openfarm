using FluentAssertions;
using OpenFarm.Application.Parts;

namespace OpenFarm.Application.Tests.Parts;

// TDD (research.md §12) — small, deterministic, well-specified grouping logic.
public class FilamentRequirementGrouperTests
{
    [Fact]
    public void Group_SingleUniformMaterial_ProducesOneRequirement()
    {
        var slots = new[]
        {
            new DetectedFilamentSlot("PETG", "#000000", "Bambu PETG-CF"),
            new DetectedFilamentSlot("PETG", "#000000", "Bambu PETG-CF"),
            new DetectedFilamentSlot("PETG", "#000000", "Bambu PETG-CF"),
        };

        var result = FilamentRequirementGrouper.Group(slots);

        result.Should().ContainSingle();
        result[0].RequirementIndex.Should().Be(0);
        result[0].MaterialType.Should().Be("PETG");
        result[0].DetectedColorHex.Should().Be("#000000");
    }

    [Fact]
    public void Group_FourPlateWorkedExample_ProducesThreeDistinctRequirementsInFirstSeenOrder()
    {
        // The user's own worked example (research.md §7): plates 1/3 share black PETG, plate 2 is
        // white PLA, plate 4 is red PETG — 4 plates, 3 distinct (material, color) pairs.
        var slots = new[]
        {
            new DetectedFilamentSlot("PETG", "#000000", null), // plate 1
            new DetectedFilamentSlot("PLA", "#FFFFFF", null),  // plate 2
            new DetectedFilamentSlot("PETG", "#000000", null), // plate 3 (repeat of plate 1's pair)
            new DetectedFilamentSlot("PETG", "#FF0000", null), // plate 4
        };

        var result = FilamentRequirementGrouper.Group(slots);

        result.Should().HaveCount(3);
        result[0].MaterialType.Should().Be("PETG");
        result[0].DetectedColorHex.Should().Be("#000000");
        result[0].RequirementIndex.Should().Be(0);
        result[1].MaterialType.Should().Be("PLA");
        result[1].DetectedColorHex.Should().Be("#FFFFFF");
        result[1].RequirementIndex.Should().Be(1);
        result[2].MaterialType.Should().Be("PETG");
        result[2].DetectedColorHex.Should().Be("#FF0000");
        result[2].RequirementIndex.Should().Be(2);
    }

    [Fact]
    public void Group_SameMaterialDifferentColor_ProducesSeparateRequirements()
    {
        var slots = new[]
        {
            new DetectedFilamentSlot("PLA", "#000000", null),
            new DetectedFilamentSlot("PLA", "#FFFFFF", null),
        };

        var result = FilamentRequirementGrouper.Group(slots);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Group_NullColorTreatedAsItsOwnDistinctValue()
    {
        var slots = new[]
        {
            new DetectedFilamentSlot("PLA", null, null),
            new DetectedFilamentSlot("PLA", null, null),
            new DetectedFilamentSlot("PLA", "#000000", null),
        };

        var result = FilamentRequirementGrouper.Group(slots);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Group_EmptyInput_ProducesNoRequirements()
    {
        var result = FilamentRequirementGrouper.Group([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Group_PreservesFirstSeenDetectedSubBrand()
    {
        var slots = new[]
        {
            new DetectedFilamentSlot("PETG", "#000000", "Bambu PETG-CF"),
            new DetectedFilamentSlot("PETG", "#000000", "A Different Sub-Brand"),
        };

        var result = FilamentRequirementGrouper.Group(slots);

        result.Should().ContainSingle();
        result[0].DetectedSubBrand.Should().Be("Bambu PETG-CF");
    }
}
