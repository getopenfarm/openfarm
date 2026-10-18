using FluentAssertions;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Tests.Production;

public class ProductionJobRequirementsTests
{
    [Fact]
    public void SnapshotKeepsVariantWhileFollowLatestUsesTheCurrentPartSelection()
    {
        var originalVariant = Guid.NewGuid();
        var part = Part.Create("Part", PartFileFormat.GCode, false, "part.gcode", "/part.gcode",
            10, 0.4m, null, null, EstimateStatus.Available, 10, 10, 1, 1, new string('a', 64),
            [new("PETG", "#000000", null, originalVariant, null)], [], []);
        var job = PrintJob.Create("Test part", Guid.NewGuid(), 1024, 1, AssignmentMode.Unassigned, null,
            part.RevisionHash, part.RevisionNumber, ProductionJobRequirements.FromPart(part));
        var replacement = Guid.NewGuid();
        part.NarrowFilamentRequirement(part.FilamentRequirements.Single().Id, replacement, null);

        ProductionJobRequirements.Resolve(job, part, RevisionMode.Snapshot).Single().NarrowFilamentVariantId.Should().Be(originalVariant);
        ProductionJobRequirements.Resolve(job, part, RevisionMode.FollowLatest).Single().NarrowFilamentVariantId.Should().Be(replacement);
        job.SetFilamentRequirements([new("PETG", "#ffffff")]);
        ProductionJobRequirements.Resolve(job, part, RevisionMode.FollowLatest).Single().DetectedColorHex.Should().Be("#ffffff");
        job.HasFilamentRequirementOverride.Should().BeTrue();
    }

    [Fact]
    public void LegacySnapshotDoesNotInferTodaysPartSelection()
    {
        var part = Part.Create("Part", PartFileFormat.GCode, false, "part.gcode", "/part.gcode",
            10, 0.4m, null, null, EstimateStatus.Available, 10, 10, 1, 1, new string('a', 64),
            [new("PETG", "#000000", null, Guid.NewGuid(), null)], [], []);
        var job = PrintJob.Create("Test part", Guid.NewGuid(), 1024, 1, AssignmentMode.Unassigned, null,
            part.RevisionHash, part.RevisionNumber, [new("PETG", "#000000")]);
        ProductionJobRequirements.Resolve(job, part, RevisionMode.Snapshot).Single().NarrowFilamentVariantId.Should().BeNull();
        job.Dispatch(Guid.NewGuid());
        ProductionJobRequirements.Resolve(job, part, RevisionMode.FollowLatest).Single().NarrowFilamentVariantId.Should().BeNull();
    }
}
