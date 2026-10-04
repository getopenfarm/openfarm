using System.IO.Compression;
using System.Text;
using FluentAssertions;
using OpenFarm.Application.Parts;

namespace OpenFarm.Application.Tests.Parts;

public class SlicedFileUnitsPerPrintTests
{
    [Theory]
    [InlineData("; object_count = 4\n", 4)]
    [InlineData("EXCLUDE_OBJECT_DEFINE NAME=a\nEXCLUDE_OBJECT_DEFINE NAME=b\n", 2)]
    [InlineData("; printing object part id:0 copy 0\n; printing object part id:0 copy 1\n; printing object part id:0 copy 0\n", 2)]
    [InlineData("; nozzle_diameter = 0.4\n", 1)]
    [InlineData("; object_count = 0\n", 1)]
    public void Gcode_SuggestsDistinctObjectsOrDefault(string text, int expected)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        SlicedFileUnitsPerPrint.Suggest(stream, "plate.gcode").Should().Be(expected);
    }

    [Theory]
    [InlineData("<config><plate><model_instance/><model_instance/></plate></config>", 2)]
    [InlineData("<config><plate><object/><object/></plate><plate><object/></plate></config>", 1)]
    [InlineData("not xml", 1)]
    public void Sliced3mf_UsesUniformPerPlateCount(string xml, int expected)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("Metadata/model_settings.config").Open())) writer.Write(xml);
        stream.Position = 0;
        SlicedFileUnitsPerPrint.Suggest(stream, "plates.3mf").Should().Be(expected);
    }
}
