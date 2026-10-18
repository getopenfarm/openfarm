using System.IO.Compression;
using System.Text;
using FluentAssertions;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Infrastructure.Tests.Production;

public class JobFilamentUsageReaderTests
{
    private static byte[] ThreeMf(params (string Name, string Content)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
                using (var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(content);
        return output.ToArray();
    }

    private const string TwoColourSliceInfo = """
        <config>
          <plate>
            <metadata key="index" value="1"/>
            <filament id="1" type="PLA" color="#ff0000" used_m="4.0" used_g="12.5"/>
            <filament id="2" type="PLA" color="#FFFFFF" used_m="1.0" used_g="3.25"/>
            <filament id="3" type="PETG" color="#000000" used_m="0" used_g="0"/>
          </plate>
          <plate>
            <metadata key="index" value="2"/>
            <filament id="1" type="PLA" color="#00FF00" used_g="99"/>
          </plate>
        </config>
        """;

    [Fact]
    public void Read_ThreeMf_ReturnsPlateOnePerFilamentUsage_SkippingUnusedFilaments()
    {
        var file = ThreeMf(("Metadata/slice_info.config", TwoColourSliceInfo), ("Metadata/plate_1.gcode", "G1"));

        JobFilamentUsageReader.Read(file).Usages.Should().Equal(
            new FilamentUsage("PLA", "#FF0000", 12.5m, 0), new FilamentUsage("PLA", "#FFFFFF", 3.25m, 1));
    }

    [Fact]
    public void ReadAllPlates_SumsEveryPlateByMaterialAndColour()
    {
        var file = ThreeMf(("Metadata/slice_info.config", TwoColourSliceInfo),
            ("Metadata/plate_1.gcode", "G1"), ("Metadata/plate_2.gcode", "G1"));

        JobFilamentUsageReader.ReadAllPlates(file).Should().BeEquivalentTo(
        [
            new FilamentGrams("PLA", "#FF0000", 12.5m), new FilamentGrams("PLA", "#FFFFFF", 3.25m),
            new FilamentGrams("PLA", "#00FF00", 99m),
        ]);
    }

    [Fact]
    public void ReadAllPlates_UnreadableFile_IsEmpty()
    {
        JobFilamentUsageReader.ReadAllPlates([(byte)'P', (byte)'K', 1, 2, 3]).Should().BeEmpty();
    }

    [Fact]
    public void Read_ThreeMfWithoutSliceInfo_FallsBackToPlateGCodeFooter()
    {
        var gcode = "; filament_type = PLA;PETG\n; filament_colour = #FF0000;#0000FF\n; filament used [g] = 0.00, 7.50\n";
        var file = ThreeMf(("Metadata/plate_1.gcode", gcode));

        JobFilamentUsageReader.Read(file).Usages.Should().Equal(new FilamentUsage("PETG", "#0000FF", 7.5m, 1));
    }

    [Fact]
    public void Read_BareGCode_UsesFooter()
    {
        var gcode = Encoding.UTF8.GetBytes("; filament_type = PLA\n; filament_colour = #00FF00\n; filament used [g] = 18.4\n");

        JobFilamentUsageReader.Read(gcode).Usages.Should().Equal(new FilamentUsage("PLA", "#00FF00", 18.4m, 0));
    }

    [Fact]
    public void Read_UnreadableOrSilentFile_IsEmptyNotAnError()
    {
        JobFilamentUsageReader.Read([(byte)'P', (byte)'K', 1, 2, 3]).Usages.Should().BeEmpty();
        JobFilamentUsageReader.Read(Encoding.UTF8.GetBytes("G1 X0 Y0")).Usages.Should().BeEmpty();
        JobFilamentUsageReader.Read(ThreeMf(("Metadata/slice_info.config", "<config><plate/>"))).Usages.Should().BeEmpty();
    }

    [Fact]
    public void Read_ThreeMf_AlsoReadsTheLayerCurvePerTool()
    {
        const string gcode = "M83\nT0\n; layer num/total_layer_count: 1/2\nG1 E3\n; layer num/total_layer_count: 2/2\nT1\nG1 E5\n";
        var file = ThreeMf(("Metadata/slice_info.config", TwoColourSliceInfo), ("Metadata/plate_1.gcode", gcode));

        var report = JobFilamentUsageReader.Read(file);

        report.Extrusion.Tools[0].CumulativeMm.Should().Equal(3, 3);
        report.Extrusion.Tools[1].CumulativeMm.Should().Equal(0, 5);
    }

    [Fact]
    public void Read_BareGCode_AlsoReadsTheLayerCurve()
    {
        var gcode = Encoding.UTF8.GetBytes("; filament_type = PLA\n; filament_colour = #00FF00\n; filament used [g] = 18.4\nM83\nT0\n; layer num/total_layer_count: 1/1\nG1 E2\n");

        JobFilamentUsageReader.Read(gcode).Extrusion.Tools[0].CumulativeMm.Should().Equal(2);
    }

    /// <summary>Real OrcaSlicer 2.4.2 two-colour print (Bambu P1S, black layers 1-31, white/black interleaved after).</summary>
    [Fact]
    public void Read_RealMultiColourPrint_PlacesEachColourAndAddsTheStartPurge()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Parts", "Fixtures", "multicolor-vertical-petg.gcode.3mf");

        var report = JobFilamentUsageReader.Read(File.ReadAllBytes(path));

        // Slicer says 19.28 g and 32.00 g; the machine start G-code purges ~0.4 g of the first filament before layer 1 on top.
        report.Usages.Single(u => u.ToolIndex == 0).Grams.Should().BeInRange(19.5m, 19.9m);
        report.Usages.Single(u => u.ToolIndex == 1).Grams.Should().Be(32.00m);
        report.Extrusion.Tools[0].PrimeMm.Should().BeInRange(100, 180);
        report.Extrusion.Tools[1].PrimeMm.Should().Be(0);

        // The second colour first appears in layer 32: nothing of it is used before that.
        report.Extrusion.Tools[1].CumulativeMm[30].Should().Be(0);
        report.Extrusion.Tools[1].CumulativeMm[31].Should().BeGreaterThan(0);

        // A stop in layer 20 takes none of the second colour; a stop at layer 32 takes part of it, below the whole.
        var white = FilamentUsageCurve.FromExtrusion(report.Extrusion.Marks, [report.Extrusion.Tools[1]])!;
        white.ShareAt(20, 40).Should().Be(0);
        white.ShareAt(32).Should().BeInRange(0.001, 0.99);
    }

    private const string PlateFourSliceInfo = """
        <config>
          <plate>
            <metadata key="index" value="4"/>
            <filament id="1" type="PETG" color="#000000" used_m="50" used_g="171.4"/>
          </plate>
        </config>
        """;

    /// <summary>What the printer holds after "print plate 4" from a multi-plate project: only that plate, still numbered 4.</summary>
    private static byte[] PlateFourOnly() =>
        ThreeMf(("Metadata/slice_info.config", PlateFourSliceInfo), ("Metadata/plate_4.gcode", "G1"));

    [Fact]
    public void ChoosePlate_FileHoldingOnePlate_UsesThatPlate_WhateverTheNameSays()
    {
        var file = PlateFourOnly();

        JobFilamentUsageReader.ChoosePlate(file, namedPlate: null).Should().Be(4);
        JobFilamentUsageReader.ChoosePlate(file, namedPlate: 1).Should().Be(4);
    }

    [Fact]
    public void Read_PlateTheFileHolds_FindsItsFilament_WherePlateOneFindsNone()
    {
        var file = PlateFourOnly();

        JobFilamentUsageReader.Read(file).Usages.Should().BeEmpty("the default plate 1 is not in this file");
        JobFilamentUsageReader.Read(file, JobFilamentUsageReader.ChoosePlate(file, null)!.Value).Usages
            .Should().ContainSingle(u => u.MaterialType == "PETG" && u.Grams == 171.4m);
    }

    [Fact]
    public void ChoosePlate_SeveralPlates_PrefersTheOneThePrinterNames_ElseGivesUp()
    {
        var file = ThreeMf(("Metadata/plate_1.gcode", "G1"), ("Metadata/plate_2.gcode", "G1"));

        JobFilamentUsageReader.ChoosePlate(file, namedPlate: 2).Should().Be(2);
        JobFilamentUsageReader.ChoosePlate(file, namedPlate: null).Should().BeNull();
        JobFilamentUsageReader.ChoosePlate(file, namedPlate: 7).Should().BeNull("plate 7 is not in the file");
    }

    [Fact]
    public void ChoosePlate_NoPlateGCodeToGoBy_FallsBackToTheNamedPlateThenPlateOne()
    {
        JobFilamentUsageReader.ChoosePlate(ThreeMf(("Metadata/slice_info.config", TwoColourSliceInfo)), namedPlate: 3).Should().Be(3);
        JobFilamentUsageReader.ChoosePlate(ThreeMf(("Metadata/slice_info.config", TwoColourSliceInfo)), namedPlate: null).Should().Be(1);
        JobFilamentUsageReader.ChoosePlate(Encoding.UTF8.GetBytes("G1 X0"), namedPlate: null).Should().Be(1);
        JobFilamentUsageReader.ChoosePlate([(byte)'P', (byte)'K', 1, 2], namedPlate: null).Should().Be(1);
    }

    [Fact]
    public void PlatesWithGCode_ListsPlateNumbersInOrder_IgnoringOtherEntries()
    {
        var file = ThreeMf(("Metadata/plate_10.gcode", "G1"), ("Metadata/plate_2.gcode", "G1"), ("Metadata/plate_2.png", "x"), ("Metadata/slice_info.config", "<config/>"));

        JobFilamentUsageReader.PlatesWithGCode(file).Should().Equal(2, 10);
    }
}
