using System.IO.Compression;
using FluentAssertions;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts;

// TDD (research.md §12) — IPartFileClassifier's dispatch rule is small, deterministic, well-specified.
public class PartFileClassifierTests
{
    private static readonly PartFileClassifier Classifier = new();

    [Theory]
    [InlineData("bracket.stl")]
    [InlineData("BRACKET.STL")]
    public void Classify_Stl_AlwaysRequiresSlicing(string fileName)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("solid test\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid test"));

        var result = Classifier.Classify(stream, fileName);

        result.Format.Should().Be(PartFileFormat.Stl);
        result.RequiresSlicing.Should().BeTrue();
    }

    [Theory]
    [InlineData("bracket.gcode")]
    [InlineData("bracket.gco")]
    [InlineData("bracket.g")]
    public void Classify_GCode_NeverRequiresSlicing(string fileName)
    {
        using var stream = new MemoryStream([1, 2, 3]);

        var result = Classifier.Classify(stream, fileName);

        result.Format.Should().Be(PartFileFormat.GCode);
        result.RequiresSlicing.Should().BeFalse();
    }

    [Fact]
    public void Classify_ThreeMfWithPlateGCode_DoesNotRequireSlicing()
    {
        using var stream = BuildZip(("Metadata/plate_1.gcode", "G28"));

        var result = Classifier.Classify(stream, "widget.gcode.3mf");

        result.Format.Should().Be(PartFileFormat.ThreeMf);
        result.RequiresSlicing.Should().BeFalse();
    }

    [Fact]
    public void Classify_ThreeMfWithoutSliceInfoConfig_RequiresSlicing()
    {
        using var stream = BuildZip(("3D/3dmodel.model", "<model><resources/><build/></model>"), ("Metadata/slice_info.config", "<config/>"));

        var result = Classifier.Classify(stream, "widget.3mf");

        result.Format.Should().Be(PartFileFormat.ThreeMf);
        result.RequiresSlicing.Should().BeTrue();
    }

    [Fact]
    public void Classify_UnrecognizedExtension_Throws()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        var act = () => Classifier.Classify(stream, "widget.obj");

        act.Should().Throw<UnsupportedPartFileFormatException>();
    }

    private static MemoryStream BuildZip(params (string EntryName, string Content)[] entries)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryName, content) in entries)
            {
                var entry = archive.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        buffer.Position = 0;
        return buffer;
    }
}
