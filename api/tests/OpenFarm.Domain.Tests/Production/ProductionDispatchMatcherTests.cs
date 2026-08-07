using FluentAssertions;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

public class ProductionDispatchMatcherTests
{
    private static DispatchCandidate Candidate(
        Guid jobId, double queuePosition, decimal nozzleDiameterMm, params (string? MaterialType, string? ColorHex)[] filament) =>
        new(jobId, queuePosition, nozzleDiameterMm, filament);

    [Fact]
    public void SelectNextJob_MatchingNozzleAndMaterial_SelectsTheCandidate()
    {
        var jobId = Guid.NewGuid();
        var candidates = new[] { Candidate(jobId, 1024, 0.4m, ("PETG", "#000000")) };

        var result = ProductionDispatchMatcher.SelectNextJob(
            printerNozzleDiameterMm: 0.4m,
            printerLoadedMaterials: [("PETG", "#000000")],
            candidates: candidates);

        result.Should().Be(jobId);
    }

    [Fact]
    public void SelectNextJob_NonMatchingNozzleDiameter_SelectsNothing()
    {
        var candidates = new[] { Candidate(Guid.NewGuid(), 1024, 0.4m, ("PETG", "#000000")) };

        var result = ProductionDispatchMatcher.SelectNextJob(
            printerNozzleDiameterMm: 0.6m,
            printerLoadedMaterials: [("PETG", "#000000")],
            candidates: candidates);

        result.Should().BeNull();
    }

    [Fact]
    public void SelectNextJob_NonMatchingMaterial_SelectsNothing()
    {
        var candidates = new[] { Candidate(Guid.NewGuid(), 1024, 0.4m, ("PETG", "#000000")) };

        var result = ProductionDispatchMatcher.SelectNextJob(
            printerNozzleDiameterMm: 0.4m,
            printerLoadedMaterials: [("PLA", "#FFFFFF")],
            candidates: candidates);

        result.Should().BeNull();
    }

    [Fact]
    public void SelectNextJob_MultipleMatchingCandidates_SelectsTopmostByQueuePosition()
    {
        var lowerJobId = Guid.NewGuid();
        var candidates = new[]
        {
            Candidate(Guid.NewGuid(), 3072, 0.4m, ("PETG", "#000000")),
            Candidate(lowerJobId, 1024, 0.4m, ("PETG", "#000000")),
            Candidate(Guid.NewGuid(), 2048, 0.4m, ("PETG", "#000000")),
        };

        var result = ProductionDispatchMatcher.SelectNextJob(
            printerNozzleDiameterMm: 0.4m,
            printerLoadedMaterials: [("PETG", "#000000")],
            candidates: candidates);

        result.Should().Be(lowerJobId);
    }

    [Fact]
    public void SelectNextJob_NoCandidates_ReturnsNull()
    {
        var result = ProductionDispatchMatcher.SelectNextJob(
            printerNozzleDiameterMm: 0.4m,
            printerLoadedMaterials: [("PETG", "#000000")],
            candidates: []);

        result.Should().BeNull();
    }

    [Fact]
    public void SelectNextJob_NullMaterialTypeRequirement_MatchesAnyLoadedMaterial()
    {
        // A candidate whose requirement carries a null MaterialType represents Phase 4's "any
        // material" wildcard (an STL-only part with no Filament specified at upload) — it must match
        // regardless of what's actually loaded.
        var jobId = Guid.NewGuid();
        var candidates = new[] { Candidate(jobId, 1024, 0.4m, (null, null)) };

        var result = ProductionDispatchMatcher.SelectNextJob(
            printerNozzleDiameterMm: 0.4m,
            printerLoadedMaterials: [("ABS", "#123456")],
            candidates: candidates);

        result.Should().Be(jobId);
    }

    [Fact]
    public void SelectNextJob_MultiMaterialRequirement_RequiresEveryPairToHaveAMatch()
    {
        // A multi-color AMS part (FR-013b's collection requirement) only qualifies once every
        // required (material, color) pair is satisfiable by something currently loaded.
        var jobId = Guid.NewGuid();
        var candidates = new[] { Candidate(jobId, 1024, 0.4m, ("PETG", "#000000"), ("PETG", "#FFFFFF")) };

        var partialMatchResult = ProductionDispatchMatcher.SelectNextJob(
            printerNozzleDiameterMm: 0.4m,
            printerLoadedMaterials: [("PETG", "#000000")],
            candidates: candidates);
        partialMatchResult.Should().BeNull();

        var fullMatchResult = ProductionDispatchMatcher.SelectNextJob(
            printerNozzleDiameterMm: 0.4m,
            printerLoadedMaterials: [("PETG", "#000000"), ("PETG", "#FFFFFF")],
            candidates: candidates);
        fullMatchResult.Should().Be(jobId);
    }
}
