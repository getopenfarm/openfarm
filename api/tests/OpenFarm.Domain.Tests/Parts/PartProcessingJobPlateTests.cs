using FluentAssertions;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Domain.Tests.Parts;

// A part prints one plate of its file: the job keeps each plate's own requirements and estimate.
public class PartProcessingJobPlateTests
{
    private static readonly PreviewFilamentRequirement Black = new(0, "PETG", "#000000", null);
    private static readonly PreviewFilamentRequirement White = new(1, "PLA", "#FFFFFF", null);

    private static PartProcessingJob Ready(EstimateStatus status, params PreviewPlate[] plates)
    {
        var job = PartProcessingJob.CreateAdd(PartFileFormat.ThreeMf, "kit.3mf", "kit.3mf", 1, false, null, null, null, null, null);
        job.MarkReadyForReview(0.4m, [Black, White], plates, status, null, null, []);
        return job;
    }

    [Fact]
    public void PlateRequirements_AreThePlatesOwnWithItsOwnGrams()
    {
        var job = Ready(EstimateStatus.Available, new PreviewPlate(1, null, [0]), new PreviewPlate(2, "Lid", [0, 1], 30, 9m,
            [new FilamentGrams("PETG", "#000000", 2m), new FilamentGrams("PLA", "#FFFFFF", 7m)]));

        job.PlateRequirements(job.PreviewPlates[1]).Select(r => (r.RequirementIndex, r.EstimatedGrams)).Should().Equal((0, 2m), (1, 7m));
        job.PlateRequirements(job.PreviewPlates[0]).Should().ContainSingle(r => r.RequirementIndex == 0);
    }

    [Fact]
    public void ResolveEstimate_GivesEachPlateItsOwnSlicedNumbers()
    {
        var job = Ready(EstimateStatus.Resolving, new PreviewPlate(1, null, [0]), new PreviewPlate(2, null, [1]));

        job.ResolveEstimate(50, 15m, [], plates: [new PreviewPlate(2, null, [], 30, 9m)]);

        job.PreviewPlates.Select(p => (p.Number, p.EstimatedPrintTimeMinutes, p.EstimatedFilamentWeightGrams))
            .Should().Equal((1, null, null), (2, 30, 9m));
    }

    [Fact]
    public void ResolveEstimate_ASinglePlateWithoutItsOwnNumbers_TakesTheTotals()
    {
        var job = Ready(EstimateStatus.Resolving, new PreviewPlate(1, null, [0, 1]));

        job.ResolveEstimate(50, 15m, []);

        job.PreviewPlates.Should().ContainSingle().Which.Should().BeEquivalentTo(new { EstimatedPrintTimeMinutes = 50, EstimatedFilamentWeightGrams = 15m });
    }
}
