using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Infrastructure.Tests.Production;

// Which line's filament each slot of a multi-colour project slices with: the slot's material and colour name the line,
// the same key the part's requirements were grouped by.
public class JobFileFilamentSlotTests
{
    private static readonly JobFilamentRequirementSpec White = new("PLA", "#FFFFFF");
    private static readonly JobFilamentRequirementSpec Black = new("PLA", "#000000");

    [Fact]
    public void EachSlot_TakesTheLineWithItsMaterialAndColour()
    {
        var slots = JobFileResolver.FilamentSlots(
            [new DetectedFilamentSlot("PLA", "#000000", null), new DetectedFilamentSlot("pla", "#ffffff", null), new DetectedFilamentSlot("PLA", "#000000", null)],
            [White, Black]);

        slots.Should().Equal(new SlotFilament(1, "#000000"), new SlotFilament(0, "#ffffff"), new SlotFilament(1, "#000000"));
    }

    [Fact]
    public void ABlankSlot_IsUnusedAndTakesTheFirstFilament()
    {
        var slots = JobFileResolver.FilamentSlots([null, new DetectedFilamentSlot("PLA", "#000000", null)], [Black]);

        slots.Should().Equal(new SlotFilament(0, null), new SlotFilament(0, "#000000"));
    }

    [Fact]
    public void ASlotNoLineClaims_BlocksTheJob()
    {
        var act = () => JobFileResolver.FilamentSlots([new DetectedFilamentSlot("PETG", "#FF0000", null)], [White]);

        act.Should().Throw<SlicingProfileUnconfiguredException>().WithMessage("Filament 1 of the file (PETG #FF0000) matches no job line.*");
    }

    [Fact]
    public void AFileWithoutSlots_SlicesWithItsOneLine_AndCannotCarrySeveral()
    {
        JobFileResolver.FilamentSlots([], [White]).Should().BeEmpty();

        var act = () => JobFileResolver.FilamentSlots([], [White, Black]);
        act.Should().Throw<SlicingProfileUnconfiguredException>();
    }
}
