using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.Profiles;
using OpenFarm.Infrastructure.Profiles;

namespace OpenFarm.Infrastructure.Tests.Profiles;

// TDD (write-first) per plan.md's Constitution Check — the vendor-index -> System-Profile field
// mapping is deterministic, well-specified logic, verified against the real T001/T002 fixtures
// (research.md §0/§0a).
public class VendorProfileIndexReaderTests
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Profiles", "Fixtures", "VendorIndex");

    private static readonly VendorProfileIndexReader Reader = new(NullLogger<VendorProfileIndexReader>.Instance);

    private static string OrcaIndexPath => Path.Combine(FixturesRoot, "OrcaSlicer", "BBL.json");
    private static string BambuStudioIndexPath => Path.Combine(FixturesRoot, "BambuStudio", "BBL.json");

    [Fact]
    public void ReadSummary_OrcaSlicerIndex_ReturnsVendorNameAndMachineModelNames()
    {
        var summary = Reader.ReadSummary(OrcaIndexPath);

        summary.VendorName.Should().Be("Bambulab");
        summary.MachineModelNames.Should().Contain("Bambu Lab X1 Carbon");
    }

    [Fact]
    public void ReadPresets_MachineKind_LeafPresetMapsSettingIdAndPrinterModelAndIsSelectable()
    {
        var result = Reader.ReadPresets(OrcaIndexPath, ProfileKind.Machine);

        var leaf = result.Presets.Should().ContainSingle(p => p.Name == "Bambu Lab X1 Carbon 0.4 nozzle").Subject;

        leaf.ExternalId.Should().Be("GM001");
        leaf.IsSelectable.Should().BeTrue();
        leaf.VendorMachineModelName.Should().Be("Bambu Lab X1 Carbon");
        leaf.BaseProfileName.Should().Be("fdm_bbl_3dp_001_common");
    }

    [Fact]
    public void ReadPresets_MachineKind_AbstractBasePresetHasNoExternalIdAndIsNotSelectable()
    {
        var result = Reader.ReadPresets(OrcaIndexPath, ProfileKind.Machine);

        var abstractBase = result.Presets.Should().ContainSingle(p => p.Name == "fdm_bbl_3dp_001_common").Subject;

        abstractBase.ExternalId.Should().BeNull();
        abstractBase.IsSelectable.Should().BeFalse();
    }

    [Fact]
    public void ReadPresets_ProcessKind_MapsCompatibleMachineNamesAndSettingId()
    {
        var result = Reader.ReadPresets(OrcaIndexPath, ProfileKind.Process);

        var leaf = result.Presets.Should().ContainSingle(p => p.Name == "0.20mm Standard @BBL X1C").Subject;

        leaf.ExternalId.Should().Be("GP004");
        leaf.IsSelectable.Should().BeTrue();
        leaf.CompatibleMachineNames.Should().Contain("Bambu Lab X1 Carbon 0.4 nozzle");
    }

    [Fact]
    public void ReadPresets_FilamentKind_ResolvesMaterialTypeByWalkingAncestorChainAtReadTime()
    {
        var result = Reader.ReadPresets(OrcaIndexPath, ProfileKind.Filament);

        // "Bambu PLA Basic @BBL X1C" never declares filament_type itself — it's set once on
        // fdm_filament_common (the root of its chain) and must be resolved by walking the chain,
        // not read directly off the leaf's own JSON (research.md §0 point 5).
        var leaf = result.Presets.Should().ContainSingle(p => p.Name == "Bambu PLA Basic @BBL X1C").Subject;

        leaf.MaterialType.Should().Be("PLA");
        leaf.ExternalId.Should().Be("GFSA00");
    }

    [Fact]
    public void ReadPresets_FilamentKind_ResolvesConsistentlyAcrossRepeatedCalls()
    {
        var first = Reader.ReadPresets(OrcaIndexPath, ProfileKind.Filament);
        var second = Reader.ReadPresets(OrcaIndexPath, ProfileKind.Filament);

        first.Presets.Single(p => p.Name == "Bambu PLA Basic @BBL X1C").MaterialType
            .Should().Be(second.Presets.Single(p => p.Name == "Bambu PLA Basic @BBL X1C").MaterialType);
    }

    [Fact]
    public void ReadPresets_ProcessKind_RenamedLeafStillCarriesItsStableExternalId()
    {
        // "0.20mm Standard @BBL X1C" carries a real renamed_from field (research.md §1) — confirmed
        // here since ExternalId (not Name) is what ingestion upserts by, so the rename itself doesn't
        // change ExternalId at read time.
        var result = Reader.ReadPresets(OrcaIndexPath, ProfileKind.Process);

        var renamed = result.Presets.Single(p => p.Name == "0.20mm Standard @BBL X1C");
        renamed.ExternalId.Should().Be("GP004");
    }

    [Fact]
    public void ReadPresets_BambuStudioMachineKind_UntypedIncludeTargetFragmentsAreNotSelectable()
    {
        var result = Reader.ReadPresets(BambuStudioIndexPath, ProfileKind.Machine);

        // Confirmed real gap (research.md §0a): Bambu Studio's machine_list mixes real type:"machine"
        // presets with untyped gcode-template fragments — list membership alone must never make one
        // of these look like a selectable machine preset.
        var fragment = result.Presets.Should().ContainSingle(
            p => p.Name == "Bambu Lab X1 Carbon 0.4 nozzle template machine_start_gcode").Subject;

        fragment.IsSelectable.Should().BeFalse();
    }

    [Fact]
    public void ReadPresets_BambuStudioMachineKind_RealLeafCarriesIncludedTemplateNamesAndIsSelectable()
    {
        var result = Reader.ReadPresets(BambuStudioIndexPath, ProfileKind.Machine);

        var leaf = result.Presets.Should().ContainSingle(p => p.Name == "Bambu Lab X1 Carbon 0.4 nozzle").Subject;

        leaf.IsSelectable.Should().BeTrue();
        leaf.IncludedTemplateNames.Should().Contain("Bambu Lab X1 Carbon 0.4 nozzle template machine_start_gcode");
        leaf.IncludedTemplateNames.Should().Contain("Bambu Lab X1 Carbon 0.4 nozzle template change_filament_gcode");
    }

    [Fact]
    public void ReadPresets_BambuStudioFilamentKind_IncludeTargetIsTypedButNotSelectable()
    {
        var result = Reader.ReadPresets(BambuStudioIndexPath, ProfileKind.Filament);

        // Unlike the machine-side fragments, Bambu Studio's filament include targets DO carry
        // type:"filament" (research.md §0a) — but instantiation:"false" still keeps them out of a picker.
        var leaf = result.Presets.Should().ContainSingle(p => p.Name == "Bambu PLA Dynamic @BBL X1C 0.2 nozzle").Subject;
        leaf.IncludedTemplateNames.Should().Contain("fdm_filament_template_direct_dual");

        var fragment = result.Presets.Should().ContainSingle(p => p.Name == "fdm_filament_template_direct_dual").Subject;
        fragment.IsSelectable.Should().BeFalse();
    }

    [Fact]
    public void ReadPresets_MissingReferencedFile_SkipsItWithoutAbortingTheRestOfTheVendor()
    {
        var tempDir = Directory.CreateTempSubdirectory("vendor-index-missing-file-");
        try
        {
            var indexPath = Path.Combine(tempDir.FullName, "BBL.json");
            File.WriteAllText(indexPath, """
                {
                  "name": "Bambulab",
                  "machine_model_list": [],
                  "machine_list": [
                    { "name": "does-not-exist", "sub_path": "machine/does-not-exist.json" }
                  ],
                  "process_list": [],
                  "filament_list": []
                }
                """);

            var result = Reader.ReadPresets(indexPath, ProfileKind.Machine);

            result.Presets.Should().BeEmpty();
            result.SkippedEntries.Should().ContainSingle();
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
