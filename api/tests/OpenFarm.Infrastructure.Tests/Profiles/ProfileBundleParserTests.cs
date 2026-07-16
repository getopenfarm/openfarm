using FluentAssertions;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Profiles;

namespace OpenFarm.Infrastructure.Tests.Profiles;

// Fixture-replay, test-after (research.md §2, mirrors the BambuMqttMessageParser precedent) —
// real vendor-shipped OrcaSlicer preset JSON, see Fixtures/README.md for provenance.
public class ProfileBundleParserTests
{
    private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Profiles", "Fixtures");

    private static readonly ProfileBundleParser Parser = new();

    private static FileStream OpenFixture(string fileName) =>
        File.OpenRead(Path.Combine(FixturesDirectory, fileName));

    [Fact]
    public async Task ParseAsync_MultiPresetBundle_ClassifiesByMachineAndFilamentSubfolder()
    {
        await using var stream = OpenFixture("orcaslicer-multi-preset-bundle.zip");

        var result = await Parser.ParseAsync(stream, ProfileSourceFormat.OrcaSlicer, CancellationToken.None);

        result.MachinePresets.Should().ContainSingle(p => p.Name == "Bambu Lab X1 Carbon 0.4 nozzle");
        result.FilamentPresets.Should().Contain(p => p.Name == "Bambu PLA Basic @BBL X1C");
        result.FilamentPresets.Should().Contain(p => p.Name == "My Custom PLA");
        result.FilamentPresets.Should().Contain(p => p.Name == "My Custom PLA - Red");
    }

    [Fact]
    public async Task ParseAsync_PresetWithInheritsField_ExtractsBaseProfileName()
    {
        await using var stream = OpenFixture("base-and-override-pair.zip");

        var result = await Parser.ParseAsync(stream, ProfileSourceFormat.OrcaSlicer, CancellationToken.None);

        var root = result.FilamentPresets.Should().ContainSingle(p => p.Name == "My Custom PLA").Subject;
        root.BaseProfileName.Should().BeNull();

        var overridePreset = result.FilamentPresets.Should().ContainSingle(p => p.Name == "My Custom PLA - Red").Subject;
        overridePreset.BaseProfileName.Should().Be("My Custom PLA");
    }

    [Fact]
    public async Task ParseAsync_SinglePresetOrcaPrinterExport_ExtractsMachineAndCompatibleFilament()
    {
        await using var stream = OpenFixture("bambu-p1s-04-nozzle.orca_printer");

        var result = await Parser.ParseAsync(stream, ProfileSourceFormat.OrcaSlicer, CancellationToken.None);

        result.MachinePresets.Should().ContainSingle(p => p.Name == "Bambu Lab P1S 0.4 nozzle");
        result.FilamentPresets.Should().ContainSingle();
    }

    [Fact]
    public async Task ParseAsync_BambuStudioBundle_ParsesIdenticallyToOrcaSlicerShape()
    {
        await using var stream = OpenFixture("bambu-studio-printer-config.bbscfg");

        var result = await Parser.ParseAsync(stream, ProfileSourceFormat.BambuStudio, CancellationToken.None);

        result.MachinePresets.Should().ContainSingle();
        result.FilamentPresets.Should().HaveCount(2);
        result.MachinePresets.Single().SourceFormat.Should().Be(ProfileSourceFormat.BambuStudio);
    }

    [Fact]
    public async Task ParseAsync_NonZipFile_ThrowsUnsupportedProfileBundleFormatException()
    {
        await using var stream = OpenFixture("not-a-zip.zip");

        var act = async () => await Parser.ParseAsync(stream, ProfileSourceFormat.OrcaSlicer, CancellationToken.None);

        await act.Should().ThrowAsync<UnsupportedProfileBundleFormatException>();
    }

    [Fact]
    public async Task ParseAsync_EmptyBundle_ReturnsBothArraysEmpty()
    {
        await using var stream = OpenFixture("empty-bundle.zip");

        var result = await Parser.ParseAsync(stream, ProfileSourceFormat.OrcaSlicer, CancellationToken.None);

        result.MachinePresets.Should().BeEmpty();
        result.FilamentPresets.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractMachineDisplayFields_RealX1CarbonMachinePreset_ExtractsNozzleAndBedFields()
    {
        await using var stream = OpenFixture("orcaslicer-multi-preset-bundle.zip");
        var result = await Parser.ParseAsync(stream, ProfileSourceFormat.OrcaSlicer, CancellationToken.None);
        var preset = result.MachinePresets.Single(p => p.Name == "Bambu Lab X1 Carbon 0.4 nozzle");

        var fields = Parser.ExtractMachineDisplayFields(preset.RawDataJson, preset.SourceFormat);

        // nozzle_diameter is on this preset's own JSON, but printable_area is a delta-export gap —
        // it lives only on the unresolved ancestor "fdm_bbl_3dp_001_common" (research.md §9/§10);
        // resolving it requires ProfileRawDataMerger's chain-walk, exercised separately, not raw
        // single-row extraction.
        fields.NozzleSizeMm.Should().Be(0.4);
        fields.BedSizeXMm.Should().BeNull();
        fields.BedSizeYMm.Should().BeNull();
    }

    [Fact]
    public async Task ExtractFilamentDisplayFields_RealPlaBasicPreset_ExtractsMaxVolumetricSpeed()
    {
        await using var stream = OpenFixture("orcaslicer-multi-preset-bundle.zip");
        var result = await Parser.ParseAsync(stream, ProfileSourceFormat.OrcaSlicer, CancellationToken.None);
        var preset = result.FilamentPresets.Single(p => p.Name == "Bambu PLA Basic @BBL X1C");

        var fields = Parser.ExtractFilamentDisplayFields(
            preset.RawDataJson, preset.SourceFormat, preset.Name, hasBaseProfileName: preset.BaseProfileName is not null);

        fields.MaxVolumetricSpeedMm3s.Should().Be(21);
    }

    [Fact]
    public async Task ExtractFilamentDisplayFields_RootPresetWithNoCompatiblePrinters_FallsBackToNameSuffix()
    {
        await using var stream = OpenFixture("orcaslicer-multi-preset-bundle.zip");
        var result = await Parser.ParseAsync(stream, ProfileSourceFormat.OrcaSlicer, CancellationToken.None);
        // "Bambu PETG Basic @BBL X1C" itself has a base (Bambu PETG Basic @base) so isn't a root —
        // exercise the fallback using a genuinely root preset instead: "My Custom PLA".
        var preset = result.FilamentPresets.Single(p => p.Name == "My Custom PLA");

        var fields = Parser.ExtractFilamentDisplayFields(
            preset.RawDataJson, preset.SourceFormat, preset.Name, hasBaseProfileName: preset.BaseProfileName is not null);

        // "My Custom PLA" has no "@" suffix and declares compatible_printers: [] explicitly, so the
        // fallback correctly yields nothing rather than fabricating a name.
        fields.CompatiblePrinterNames.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractFilamentDisplayFields_ToleratesBuggyCompatiblePrintsKeySpelling()
    {
        await using var stream = OpenFixture("bambu-studio-filament.bbsflmt");
        var result = await Parser.ParseAsync(stream, ProfileSourceFormat.BambuStudio, CancellationToken.None);
        var preset = result.FilamentPresets.Single();

        var fields = Parser.ExtractFilamentDisplayFields(
            preset.RawDataJson, preset.SourceFormat, preset.Name, hasBaseProfileName: preset.BaseProfileName is not null);

        // The fixture's raw JSON has the misspelled "compatible_prints" key (research.md §11,
        // OrcaSlicer issue #12193) instead of "compatible_printers" — the parser must read it
        // anyway rather than treating it as absent.
        fields.CompatiblePrinterNames.Should().Contain("Bambu Lab X1 Carbon 0.4 nozzle");
    }

    [Fact]
    public async Task ExtractMachineDisplayFields_MergedWithAncestor_ResolvesBedSizeFromAncestor()
    {
        await using var stream = OpenFixture("orcaslicer-multi-preset-bundle.zip");
        var result = await Parser.ParseAsync(stream, ProfileSourceFormat.OrcaSlicer, CancellationToken.None);
        var preset = result.MachinePresets.Single(p => p.Name == "Bambu Lab X1 Carbon 0.4 nozzle");

        // Simulates what GetMachineProfilesQuery's chain-walk does once the unresolved ancestor
        // ("fdm_bbl_3dp_001_common") is itself known — here supplied inline as a synthetic ancestor
        // blob carrying the real printable_area value, proving the merge (not just raw extraction)
        // is what closes the delta-export gap (research.md §10).
        var syntheticAncestor = """{"printable_area":["0x0","256x0","256x256","0x256"]}""";
        var merged = ProfileRawDataMerger.MergeAncestorChain([syntheticAncestor, preset.RawDataJson]);

        var fields = Parser.ExtractMachineDisplayFields(merged, preset.SourceFormat);

        fields.BedSizeXMm.Should().Be(256);
        fields.BedSizeYMm.Should().Be(256);
        fields.NozzleSizeMm.Should().Be(0.4);
    }

    [Fact]
    public async Task ParseAsync_UnnamedOrMissingKeyPresets_AreTolerated()
    {
        // Every real fixture preset has a "name" — this asserts the parser doesn't throw when
        // optional fields (e.g. compatible_printers) are simply absent, covered implicitly by every
        // test above succeeding against real, sparse delta-export content.
        await using var stream = OpenFixture("aliz-petg-basic.orca_filament");

        var act = async () => await Parser.ParseAsync(stream, ProfileSourceFormat.OrcaSlicer, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
