using FluentAssertions;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Domain.Tests.Parts;

public class PartPreviousRequirementTests
{
    private static readonly Guid Aliz = Guid.NewGuid();

    private static Part NewPart(params FilamentRequirementSpec[] requirements) =>
        Part.Create("Part", PartFileFormat.ThreeMf, false, "part.3mf", "/tmp/part.3mf", 100, 0.4m, null, null,
            EstimateStatus.Unavailable, null, null, 1, 1, new string('a', 64), requirements, [], []);

    private static PreviewFilamentRequirement Preview(int index, string material, string hex) => new(index, material, hex, null);

    [Fact]
    public void SameMaterialAndColour_IsCarried()
    {
        var part = NewPart(new FilamentRequirementSpec("PETG", "#000000", null, Guid.NewGuid(), null));
        var file = new[] { Preview(0, "PETG", "#000000") };

        part.PreviousRequirementFor(file[0], file).Should().BeSameAs(part.FilamentRequirements[0]);
    }

    [Fact]
    public void AColourFreeChoice_SurvivesAColourSwap()
    {
        var part = NewPart(new("PETG", "#000000", null, null, null, NarrowFilamentId: Aliz),
            new("PLA", "#FFFFFF", null, null, null, AnyColor: true));
        var file = new[] { Preview(0, "PETG", "#FF0000"), Preview(1, "PLA", "#00FF00") };

        part.PreviousRequirementFor(file[0], file)!.NarrowFilamentId.Should().Be(Aliz);
        part.PreviousRequirementFor(file[1], file)!.AnyColor.Should().BeTrue();
    }

    [Fact]
    public void OneColour_DoesNotSurviveAColourSwap()
    {
        var part = NewPart(new FilamentRequirementSpec("PETG", "#000000", null, Guid.NewGuid(), null));
        var file = new[] { Preview(0, "PETG", "#FF0000") };

        part.PreviousRequirementFor(file[0], file).Should().BeNull();
    }

    [Fact]
    public void AnAmbiguousSwap_CarriesNothing()
    {
        // Two PETG colours went and two came: which continues which is unknown.
        var part = NewPart(new("PETG", "#000000", null, null, null, NarrowFilamentId: Aliz),
            new("PETG", "#FFFFFF", null, null, null, AnyColor: true));
        var file = new[] { Preview(0, "PETG", "#FF0000"), Preview(1, "PETG", "#00FF00") };

        part.PreviousRequirementFor(file[0], file).Should().BeNull();
        part.PreviousRequirementFor(file[1], file).Should().BeNull();
    }

    [Fact]
    public void AnAddedColour_DoesNotTakeAKeptColoursChoice()
    {
        var part = NewPart(new FilamentRequirementSpec("PETG", "#000000", null, null, null, NarrowFilamentId: Aliz));
        var file = new[] { Preview(0, "PETG", "#000000"), Preview(1, "PETG", "#FF0000") };

        part.PreviousRequirementFor(file[1], file).Should().BeNull();
    }
}
