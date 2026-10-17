using System.IO.Compression;
using System.Text;
using FluentAssertions;
using OpenFarm.Application.Production;
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

        report.Curves[0].Should().Equal(3, 3);
        report.Curves[1].Should().Equal(0, 5);
    }

    [Fact]
    public void Read_BareGCode_AlsoReadsTheLayerCurve()
    {
        var gcode = Encoding.UTF8.GetBytes("; filament_type = PLA\n; filament_colour = #00FF00\n; filament used [g] = 18.4\nM83\nT0\n; layer num/total_layer_count: 1/1\nG1 E2\n");

        JobFilamentUsageReader.Read(gcode).Curves[0].Should().Equal(2);
    }
}
