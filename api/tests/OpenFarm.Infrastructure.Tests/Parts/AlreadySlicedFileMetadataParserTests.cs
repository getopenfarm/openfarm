using System.IO.Compression;
using System.Text;
using FluentAssertions;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts;

// Test-after / fixture-replay (research.md §12, matching BambuMqttMessageParser/ProfileBundleParser's
// existing carve-out) — actual byte-level G-code/3MF metadata parsing, including the 4-plate/
// 3-distinct-pair grouping worked example from research.md §7.
public class AlreadySlicedFileMetadataParserTests
{
    private static readonly AlreadySlicedFileMetadataParser Parser = new();

    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    [Fact]
    public async Task ParseAsync_SingleMaterialGCode_ParsesNozzleMaterialEstimateAndThumbnail()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Parts", "Fixtures", "single-material.gcode");
        await using var stream = File.OpenRead(fixturePath);

        var result = await Parser.ParseAsync(stream, PartFileFormat.GCode, CancellationToken.None);

        result.NozzleDiameterMm.Should().Be(0.4m);
        result.EstimatedPrintTimeMinutes.Should().Be(47);
        result.EstimatedFilamentWeightGrams.Should().Be(18.4m);
        result.PlateCount.Should().Be(1);
        result.DetectedFilamentSlots.Should().ContainSingle();
        result.DetectedFilamentSlots[0].MaterialType.Should().Be("PETG");
        result.DetectedFilamentSlots[0].DetectedColorHex.Should().Be("#1A1A1A");
        result.Thumbnails.Should().ContainSingle();
        result.Thumbnails[0].PlateIndex.Should().Be(0);
        result.Thumbnails[0].Bytes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ParseAsync_GCodeMissingNozzleDiameter_PreservesMaterial()
    {
        var content = "; filament_type = PETG\nG28\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var result = await Parser.ParseAsync(stream, PartFileFormat.GCode, CancellationToken.None);
        result.NozzleDiameterMm.Should().BeNull();
        result.DetectedFilamentSlots.Should().ContainSingle(r => r.MaterialType == "PETG");
        result.EstimatedPrintTimeMinutes.Should().BeNull();
        result.EstimatedFilamentWeightGrams.Should().BeNull();
    }

    [Fact]
    public async Task ParseAsync_FourPlateMultiColorGCode3mf_GroupsByDistinctMaterialColorPair()
    {
        // research.md §7's own worked example: plates 1 & 3 share black PETG (collapse to one
        // requirement), plate 2 is white PLA, plate 4 is red PETG — 4 plates, 3 distinct pairs.
        using var stream = BuildGCode3mf(
            sliceInfoConfig: "<config/>",
            plates:
            [
                (1, "PETG", "#000000", true),
                (2, "PLA", "#FFFFFF", false),
                (3, "PETG", "#000000", false),
                (4, "PETG", "#FF0000", false),
            ]);

        var result = await Parser.ParseAsync(stream, PartFileFormat.ThreeMf, CancellationToken.None);

        result.PlateCount.Should().Be(4);
        result.DetectedFilamentSlots.Should().HaveCount(4, "one raw detected slot per plate, before grouping");
        result.NozzleDiameterMm.Should().Be(0.4m);
        // Sum across every plate — each plate's estimate in the fixture is 10 min / 5 g.
        result.EstimatedPrintTimeMinutes.Should().Be(40);
        result.EstimatedFilamentWeightGrams.Should().Be(20m);
        // Thumbnail derivation is per plate, best-effort — only plate 1 got one in this fixture.
        result.Thumbnails.Should().ContainSingle();
        result.Thumbnails[0].PlateIndex.Should().Be(0);
    }

    [Fact]
    public async Task ParseAsync_ThreeMfWithNoPlateGCode_ThrowsPartFileParsingFailed()
    {
        using var stream = BuildZip(("Metadata/slice_info.config", "<config/>"));

        var act = async () => await Parser.ParseAsync(stream, PartFileFormat.ThreeMf, CancellationToken.None);

        await act.Should().ThrowAsync<PartFileParsingFailedException>();
    }

    [Fact]
    public async Task HeaderlessGCode_PreservesUnknownFields()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("G28\nG1 X10 Y10\n"));
        var result = await Parser.ParseAsync(stream, PartFileFormat.GCode, CancellationToken.None);
        result.NozzleDiameterMm.Should().BeNull();
        result.DetectedFilamentSlots.Should().BeEmpty();
        result.MaterialMetadataComplete.Should().BeFalse();
        result.EstimatedPrintTimeMinutes.Should().BeNull();
        result.EstimatedFilamentWeightGrams.Should().BeNull();
    }

    [Fact]
    public async Task PartialMetadata_PreservesWeightWithoutNozzleOrTime()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("; total filament weight [g] : 8.5\nG28\n"));
        var result = await Parser.ParseAsync(stream, PartFileFormat.GCode, CancellationToken.None);
        result.EstimatedFilamentWeightGrams.Should().Be(8.5m);
        result.EstimatedPrintTimeMinutes.Should().BeNull();
        result.NozzleDiameterMm.Should().BeNull();
    }

    [Fact]
    public async Task IncompletePlate_IsIncludedAndMakesTotalsUnknown()
    {
        using var stream = BuildZip(
            ("Metadata/plate_1.gcode", "; nozzle_diameter = 0.4\n; filament_type = PLA\n; total estimated time = 10m\n; filament used [g] = 5\nG28"),
            ("Metadata/plate_2.gcode", "; total estimated time = 20m\nG28"),
            ("Metadata/plate_2.png", "thumbnail"));
        var result = await Parser.ParseAsync(stream, PartFileFormat.ThreeMf, CancellationToken.None);
        result.PlateCount.Should().Be(2);
        result.NozzleDiameterMm.Should().BeNull();
        result.MaterialMetadataComplete.Should().BeFalse();
        result.EstimatedPrintTimeMinutes.Should().Be(30);
        result.EstimatedFilamentWeightGrams.Should().BeNull();
        result.Thumbnails.Should().ContainSingle(t => t.PlateIndex == 1);
        result.DetectedFilamentSlots.Should().ContainSingle();
    }

    [Fact]
    public async Task UnreadableGCode_StillFails()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a print file"));
        var act = async () => await Parser.ParseAsync(stream, PartFileFormat.GCode, CancellationToken.None);
        await act.Should().ThrowAsync<PartFileParsingFailedException>();
    }

    [Fact]
    public async Task MultiExtruderWeight_IsSummedAndConflictingNozzlesRemainUnknown()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("; nozzle_diameter = 0.4;0.6\n; filament_type = PLA;PETG\n; filament used [g] = 2.5;3.5\nG28"));
        var result = await Parser.ParseAsync(stream, PartFileFormat.GCode, CancellationToken.None);
        result.NozzleDiameterMm.Should().BeNull();
        result.EstimatedFilamentWeightGrams.Should().Be(6m);
        result.DetectedFilamentSlots.Should().HaveCount(2);
    }

    private static MemoryStream BuildGCode3mf(
        string sliceInfoConfig,
        (int PlateNumber, string MaterialType, string ColorHex, bool WithThumbnail)[] plates)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "Metadata/slice_info.config", sliceInfoConfig);

            foreach (var plate in plates)
            {
                var gcode = $"""
                    ; nozzle_diameter = 0.4
                    ; filament_type = {plate.MaterialType}
                    ; filament_colour = {plate.ColorHex}
                    ; total filament weight [g] = 5
                    ; total estimated time = 10m
                    G28
                    """;
                WriteEntry(archive, $"Metadata/plate_{plate.PlateNumber}.gcode", gcode);

                if (plate.WithThumbnail)
                {
                    var entry = archive.CreateEntry($"Metadata/plate_{plate.PlateNumber}.png");
                    using var entryStream = entry.Open();
                    var bytes = Convert.FromBase64String(OnePixelPngBase64);
                    entryStream.Write(bytes, 0, bytes.Length);
                }
            }
        }

        buffer.Position = 0;
        return buffer;
    }

    private static MemoryStream BuildZip(params (string EntryName, string Content)[] entries)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryName, content) in entries)
            {
                WriteEntry(archive, entryName, content);
            }
        }

        buffer.Position = 0;
        return buffer;
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
