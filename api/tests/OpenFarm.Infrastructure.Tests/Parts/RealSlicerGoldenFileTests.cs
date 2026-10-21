using OpenFarm.Domain.Printers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;
using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts;
using OpenFarm.Infrastructure.Parts.SlicerInvocation;
using OpenFarm.Infrastructure.Tests.Parts.RealSlicer;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Infrastructure.Tests.Parts;

// Drives a real slicer CLI (not a stub — see CustomWebApplicationFactory's deliberately-broken
// bundle used by every other slicing test) against a golden STL/3MF -> G-code fixture the user
// manually sliced once with the real slicer, so the actual toolpath output is verified, not just
// the estimate/error orchestration around it. Self-skips when no real slicer bundle is installed
// (RealSlicerFactAttribute) — see Parts/Fixtures/RealSlicer/README.md to add a new case.
//
// .stl cases only compare estimated print time/filament weight within tolerance, not the full
// toolpath: a bare .stl carries no object position/rotation, so OrcaSlicer has to auto-arrange it
// onto the bed, and that placement can legitimately differ between a GUI session and a headless
// CLI invocation even with identical settings — a full G-code diff would fail on placement noise,
// not real regressions. .3mf cases keep the full normalized diff, since the project file preserves
// the object's exact transform, making an identical toolpath both meaningful and achievable.
public class RealSlicerGoldenFileTests
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Parts", "Fixtures", "RealSlicer");

    [RealSlicerFact(SlicerEngine.OrcaSlicer, "2.3.1")]
    public Task OrcaSlicer231_Benchy_ProducesExpectedGCode() =>
        RunCaseAsync("orcaslicer-2.3.1-benchy-stl", SlicerEngine.OrcaSlicer, "2.3.1");

    // 69 of Orca 2.3.1's selectable BBL filaments inherit a base that only OrcaFilamentLibrary ships; before the
    // library bases were ingested every one of them failed at slice time with "unresolved ancestor".
    [RealSlicerFact(SlicerEngine.OrcaSlicer, "2.3.1")]
    public async Task OrcaSlicer231_FilamentInheritingFromTheOrcaFilamentLibrary_SlicesWithItsFlattenedChain()
    {
        var (available, reason) = RealSlicerBundleLocator.TryResolve(SlicerEngine.OrcaSlicer, "2.3.1");
        available.Should().NotBeNull(reason);
        var staging = await VendorProfileFixtureStaging.StageAndIngestAsync(SlicerEngine.OrcaSlicer, "2.3.1",
            "Bambu Lab P1S 0.4 nozzle", "0.20mm Standard @BBL X1C", "Overture Easy PLA @BBL X1C", CancellationToken.None);
        var profiles = await staging.Resolver.ResolveProfilesAsync(
            staging.MachineProfileId, staging.ProcessProfileId, staging.FilamentProfileId, BuildPlateType.TexturedPei, staging.Pin, use: null, CancellationToken.None);

        var caseDir = Path.Combine(FixturesRoot, "orcaslicer-2.3.1-benchy-stl");
        var model = await File.ReadAllBytesAsync(Path.Combine(caseDir, GoldenCaseManifest.Load(caseDir).ModelFileName));
        var gcode = await RealSlicerProcessRunner.SliceAsync(
            available!, new PrusaSliceFamilyInvocation(), model, PartFileFormat.Stl, "PLA", 0.4m, profiles, CancellationToken.None);

        var values = GCodeMetadataHelper.ParseComments(gcode);
        GCodeMetadataHelper.ParseEstimatedPrintTimeMinutes(values).Should().BeGreaterThan(0);
        GCodeMetadataHelper.ParseFilamentWeightGrams(values).Should().BeGreaterThan(0);
    }

    [RealSlicerFact(SlicerEngine.OrcaSlicer, "2.3.1")]
    public async Task OrcaSlicer231_EmbeddedProject_PreservesItsSavedSettings()
    {
        var (available, reason) = RealSlicerBundleLocator.TryResolve(SlicerEngine.OrcaSlicer, "2.3.1");
        available.Should().NotBeNull(reason);
        var directory = Path.Combine(FixturesRoot, "orcaslicer-2.3.1-benchy-3mf");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, "3DBenchy.3mf"));
        var staging = await VendorProfileFixtureStaging.StageAndIngestAsync(SlicerEngine.OrcaSlicer, "2.3.1",
            "Bambu Lab P1S 0.4 nozzle", "0.20mm Standard @BBL X1C", "Bambu PLA Basic @BBL X1C", CancellationToken.None);
        var engine = new CliSlicerEngine(new SingleSlicerCatalog(available!), new FixedPlatform(available!.Platform),
            [new PrusaSliceFamilyInvocation()], Options.Create(new SlicerOptions()), NullLogger<CliSlicerEngine>.Instance);
        using var input = new MemoryStream(bytes);
        var result = await engine.EstimateAsync(available.ToSlicerRef(), input, PartFileFormat.ThreeMf,
            new EstimationConfiguration(true, null, null, null), CancellationToken.None);
        result.EstimatedPrintTimeMinutes.Should().Be(35);
        result.EstimatedFilamentWeightGrams.Should().Be(11.09m);
        result.PlateCount.Should().Be(1);
        using var output = new MemoryStream(result.OutputFileBytes);
        var metadata = await new AlreadySlicedFileMetadataParser().ParseAsync(output, PartFileFormat.ThreeMf, CancellationToken.None);
        metadata.NozzleDiameterMm.Should().Be(0.4m);
        metadata.DetectedFilamentSlots.Should().ContainSingle(s => s.MaterialType == "PLA");
    }

    private sealed class SingleSlicerCatalog(AvailableSlicer available) : IAvailableSlicerCatalog
    {
        public IReadOnlyList<AvailableSlicer> GetAvailable() => [available];
        public AvailableSlicer? Find(SlicerRef slicerRef, SlicerPlatform platform) =>
            available.ToSlicerRef() == slicerRef && available.Platform == platform ? available : null;
    }
    private sealed class FixedPlatform(SlicerPlatform platform) : IHostPlatformProvider
    {
        public SlicerPlatform Current => platform;
    }

    private static async Task RunCaseAsync(string caseName, SlicerEngine engine, string version)
    {
        RealSlicerBundleLocator.GetManifestPinnedVersions(engine).Should().Contain(version,
            "the test case's requested version must be pinned in slicers.manifest.json for this platform, " +
            "or this test may silently exercise a different, possibly crash-prone, slicer build");

        var caseDir = Path.Combine(FixturesRoot, caseName);
        var manifest = GoldenCaseManifest.Load(caseDir);
        var (available, reason) = RealSlicerBundleLocator.TryResolve(engine, version);
        available.Should().NotBeNull(reason);

        ISlicerInvocation invocation = engine is SlicerEngine.OrcaSlicer or SlicerEngine.BambuStudio
            ? new PrusaSliceFamilyInvocation()
            : new SuperSlicerInvocation();

        ResolvedSlicingProfiles? resolvedProfiles = null;
        if (manifest.MachineProfileName is not null)
        {
            var staging = await VendorProfileFixtureStaging.StageAndIngestAsync(
                engine, version, manifest.MachineProfileName, manifest.ProcessProfileName!, manifest.FilamentProfileName!,
                CancellationToken.None);
            resolvedProfiles = await staging.Resolver.ResolveProfilesAsync(
                staging.MachineProfileId, staging.ProcessProfileId, staging.FilamentProfileId, BuildPlateType.TexturedPei, staging.Pin, use: null, CancellationToken.None);

            if (manifest.BedType is not null)
            {
                resolvedProfiles = WithBedTypeOverride(resolvedProfiles, manifest.BedType);
            }
        }

        var modelBytes = await File.ReadAllBytesAsync(Path.Combine(caseDir, manifest.ModelFileName));
        var actualGCode = await RealSlicerProcessRunner.SliceAsync(
            available!, invocation, modelBytes, manifest.ModelFileFormat,
            manifest.MaterialType, manifest.NozzleDiameterMm, resolvedProfiles, CancellationToken.None);

        var expectedGCode = await File.ReadAllTextAsync(Path.Combine(caseDir, manifest.ExpectedGCodeFileName));

        if (manifest.ModelFileFormat == PartFileFormat.ThreeMf)
        {
            var diff = GCodeNormalizer.Diff(GCodeNormalizer.Normalize(expectedGCode), GCodeNormalizer.Normalize(actualGCode));
            diff.IsMatch.Should().BeTrue($"the normalized G-code should match the golden fixture:\n{diff.Message}");
        }
        else
        {
            AssertMetadataWithinTolerance(expectedGCode, actualGCode);
        }
    }

    // curr_bed_type isn't declared in any BBL machine/process preset file (research confirmed no
    // default anywhere in the bundle) — it's a per-slice plate selection the GUI applies, so a
    // fixture sliced against a non-default plate needs it injected directly into the process JSON.
    private static ResolvedSlicingProfiles WithBedTypeOverride(ResolvedSlicingProfiles profiles, string bedType)
    {
        var processSettings = JsonNode.Parse(profiles.ProcessSettingsJson)!.AsObject();
        processSettings["curr_bed_type"] = bedType;

        return profiles with { ProcessSettingsJson = processSettings.ToJsonString() };
    }

    // 5% relative tolerance against the same estimated-print-time/filament-weight values the app
    // itself parses out of the sliced G-code (GCodeMetadataHelper, the source of SlicingResult in
    // production) — a placement-driven few-mm travel difference shouldn't move either by much, so
    // this still catches a real regression (wrong material, wrong layer height, wrong profile)
    // without depending on an exact, unreproducible object position.
    private const decimal ToleranceFraction = 0.05m;

    private static void AssertMetadataWithinTolerance(string expectedGCode, string actualGCode)
    {
        var expectedValues = GCodeMetadataHelper.ParseComments(expectedGCode);
        var actualValues = GCodeMetadataHelper.ParseComments(actualGCode);

        var expectedMinutes = GCodeMetadataHelper.ParseEstimatedPrintTimeMinutes(expectedValues);
        var actualMinutes = GCodeMetadataHelper.ParseEstimatedPrintTimeMinutes(actualValues);
        expectedMinutes.Should().NotBeNull("the golden fixture should contain a parseable estimated print time");
        actualMinutes.Should().NotBeNull("the real slice should contain a parseable estimated print time");

        var minutesTolerance = Math.Max(1, (int)Math.Round(expectedMinutes!.Value * ToleranceFraction));
        actualMinutes!.Value.Should().BeInRange(
            expectedMinutes.Value - minutesTolerance, expectedMinutes.Value + minutesTolerance,
            "estimated print time should match the golden fixture within tolerance");

        var expectedWeight = GCodeMetadataHelper.ParseFilamentWeightGrams(expectedValues);
        var actualWeight = GCodeMetadataHelper.ParseFilamentWeightGrams(actualValues);
        expectedWeight.Should().NotBeNull("the golden fixture should contain a parseable filament weight");
        actualWeight.Should().NotBeNull("the real slice should contain a parseable filament weight");

        var weightTolerance = Math.Max(0.5m, expectedWeight!.Value * ToleranceFraction);
        actualWeight!.Value.Should().BeInRange(
            expectedWeight.Value - weightTolerance, expectedWeight.Value + weightTolerance,
            "estimated filament weight should match the golden fixture within tolerance");
    }
}
