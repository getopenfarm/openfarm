using FluentAssertions;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

public class ProductionDispatchMatcherTests
{
    private static DispatchCandidate Candidate(
        Guid jobId, double queuePosition, decimal nozzleDiameterMm, params (string? MaterialType, string? ColorHex)[] filament) =>
        new(jobId, queuePosition, nozzleDiameterMm, filament);

    [Fact]
    public void ExplicitCatalogVariantRemovesDetectedColourFromCoarseMatching()
    {
        var spec = new JobFilamentRequirementSpec("PETG", "#000000", Guid.NewGuid());
        var candidate = Candidate(Guid.NewGuid(), 1024, 0.6m, spec.MatchingValues);
        ProductionDispatchMatcher.SelectNextJob(0.6m, [("PETG", "#27272a")], [candidate]).Should().Be(candidate.JobId);
        // Identity is validated separately against the active tracked spool during readiness.
        new JobFilamentRequirementSpec("PETG", "#000000").MatchingValues.ColorHex.Should().Be("#000000");
    }

    [Fact]
    public void ColourHexCasingDoesNotChangeCompatibility()
    {
        var candidate = Candidate(Guid.NewGuid(), 1024, 0.4m, ("PETG", "#abcdef"));
        ProductionDispatchMatcher.SelectNextJob(0.4m, [("PETG", "#ABCDEF")], [candidate]).Should().Be(candidate.JobId);
    }

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

    [Fact]
    public void SelectNextJob_NoUnassignedMatch_FallsBackToTopmostPinnedCandidate()
    {
        var lowerPinnedJobId = Guid.NewGuid();
        var pinnedCandidates = new[]
        {
            Candidate(Guid.NewGuid(), 3072, 0.6m, ("PLA", "#FFFFFF")),
            Candidate(lowerPinnedJobId, 1024, 0.6m, ("PLA", "#FFFFFF")),
        };

        var result = ProductionDispatchMatcher.SelectNextJob(
            printerNozzleDiameterMm: 0.4m,
            printerLoadedMaterials: [("PETG", "#000000")],
            candidates: [],
            pinnedCandidates: pinnedCandidates);

        // FR-010 applies to Pinned jobs too: incompatible jobs stay queued.
        result.Should().BeNull();
    }

    [Fact]
    public void SelectNextJob_UnassignedMatchExists_PrefersItOverAnyPinnedCandidate()
    {
        var unassignedJobId = Guid.NewGuid();
        var candidates = new[] { Candidate(unassignedJobId, 2048, 0.4m, ("PETG", "#000000")) };
        var pinnedCandidates = new[] { Candidate(Guid.NewGuid(), 1024, 0.6m, ("PLA", "#FFFFFF")) };

        var result = ProductionDispatchMatcher.SelectNextJob(
            printerNozzleDiameterMm: 0.4m,
            printerLoadedMaterials: [("PETG", "#000000")],
            candidates: candidates,
            pinnedCandidates: pinnedCandidates);

        result.Should().Be(unassignedJobId);
    }

    [Fact]
    public void SelectNextJob_MatchingPinnedJobAboveUnassigned_RespectsManualQueueOrder()
    {
        var pinnedId = Guid.NewGuid();
        var result = ProductionDispatchMatcher.SelectNextJob(0.4m, [("PLA", null)],
            [Candidate(Guid.NewGuid(), 2048, 0.4m, ("PLA", null))],
            [Candidate(pinnedId, 1024, 0.4m, ("PLA", null))]);
        result.Should().Be(pinnedId);
    }

    [Fact]
    public void IsPreferredAmongQualifyingPrinters_FewerTraysOtherPrinterQualifies_ReturnsFalse()
    {
        var thisPrinterId = Guid.NewGuid();
        var otherPrinterId = Guid.NewGuid();

        var result = ProductionDispatchMatcher.IsPreferredAmongQualifyingPrinters(
            thisPrinterId,
            printerCapabilityScore: 4,
            otherQualifyingPrinters: [(otherPrinterId, 2)]);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsPreferredAmongQualifyingPrinters_ThisPrinterHasFewestTrays_ReturnsTrue()
    {
        var thisPrinterId = Guid.NewGuid();
        var otherPrinterId = Guid.NewGuid();

        var result = ProductionDispatchMatcher.IsPreferredAmongQualifyingPrinters(
            thisPrinterId,
            printerCapabilityScore: 2,
            otherQualifyingPrinters: [(otherPrinterId, 4)]);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPreferredAmongQualifyingPrinters_FullTieOnCapability_FallsBackToLowerPrinterId()
    {
        var lowerPrinterId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var higherPrinterId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        ProductionDispatchMatcher.IsPreferredAmongQualifyingPrinters(
                lowerPrinterId, printerCapabilityScore: 2, otherQualifyingPrinters: [(higherPrinterId, 2)])
            .Should().BeTrue();

        ProductionDispatchMatcher.IsPreferredAmongQualifyingPrinters(
                higherPrinterId, printerCapabilityScore: 2, otherQualifyingPrinters: [(lowerPrinterId, 2)])
            .Should().BeFalse();
    }

    [Fact]
    public void IsPreferredAmongQualifyingPrinters_NoOtherQualifyingPrinters_ReturnsTrue()
    {
        var result = ProductionDispatchMatcher.IsPreferredAmongQualifyingPrinters(
            Guid.NewGuid(), printerCapabilityScore: 4, otherQualifyingPrinters: []);

        result.Should().BeTrue();
    }

    private static BackfillLineCandidate BackfillCandidate(
        Guid requirementId, decimal nozzleDiameterMm, int backfillPrinterCap, int? cumulativeCeiling,
        int currentOccupancy, int cumulativeUnits, params (string? MaterialType, string? ColorHex)[] filament) =>
        new(Guid.NewGuid(), requirementId, nozzleDiameterMm, filament, backfillPrinterCap, cumulativeCeiling, currentOccupancy, cumulativeUnits);

    [Fact]
    public void SelectNextBackfillLine_MatchingAndUnderCap_SelectsTheRequirement()
    {
        var requirementId = Guid.NewGuid();
        var candidates = new[]
        {
            BackfillCandidate(requirementId, 0.4m, backfillPrinterCap: 1, cumulativeCeiling: null, currentOccupancy: 0, cumulativeUnits: 0, ("PETG", "#000000")),
        };

        var result = ProductionDispatchMatcher.SelectNextBackfillLine(0.4m, [("PETG", "#000000")], candidates);

        result.Should().Be(requirementId);
    }

    [Fact]
    public void SelectNextBackfillLine_OccupancyAtCap_SelectsNothing()
    {
        var candidates = new[]
        {
            BackfillCandidate(Guid.NewGuid(), 0.4m, backfillPrinterCap: 1, cumulativeCeiling: null, currentOccupancy: 1, cumulativeUnits: 0, ("PETG", "#000000")),
        };

        var result = ProductionDispatchMatcher.SelectNextBackfillLine(0.4m, [("PETG", "#000000")], candidates);

        result.Should().BeNull();
    }

    [Fact]
    public void SelectNextBackfillLine_CumulativeUnitsAtCeiling_SelectsNothing()
    {
        var candidates = new[]
        {
            BackfillCandidate(Guid.NewGuid(), 0.4m, backfillPrinterCap: 5, cumulativeCeiling: 8, currentOccupancy: 0, cumulativeUnits: 8, ("PETG", "#000000")),
        };

        var result = ProductionDispatchMatcher.SelectNextBackfillLine(0.4m, [("PETG", "#000000")], candidates);

        result.Should().BeNull();
    }

    [Fact]
    public void SelectNextBackfillLine_NonMatchingNozzle_SelectsNothing()
    {
        var candidates = new[]
        {
            BackfillCandidate(Guid.NewGuid(), 0.4m, backfillPrinterCap: 1, cumulativeCeiling: null, currentOccupancy: 0, cumulativeUnits: 0, ("PETG", "#000000")),
        };

        var result = ProductionDispatchMatcher.SelectNextBackfillLine(0.6m, [("PETG", "#000000")], candidates);

        result.Should().BeNull();
    }

    [Fact]
    public void SelectNextBackfillLine_FirstCandidateIneligibleSecondEligible_SelectsSecond()
    {
        var eligibleRequirementId = Guid.NewGuid();
        var candidates = new[]
        {
            BackfillCandidate(Guid.NewGuid(), 0.4m, backfillPrinterCap: 1, cumulativeCeiling: null, currentOccupancy: 1, cumulativeUnits: 0, ("PETG", "#000000")),
            BackfillCandidate(eligibleRequirementId, 0.4m, backfillPrinterCap: 1, cumulativeCeiling: null, currentOccupancy: 0, cumulativeUnits: 0, ("PETG", "#000000")),
        };

        var result = ProductionDispatchMatcher.SelectNextBackfillLine(0.4m, [("PETG", "#000000")], candidates);

        result.Should().Be(eligibleRequirementId);
    }
}
