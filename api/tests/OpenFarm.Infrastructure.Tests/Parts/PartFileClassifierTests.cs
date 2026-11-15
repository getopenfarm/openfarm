using System.IO.Compression;
using FluentAssertions;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts;

// TDD (research.md §12) — IPartFileClassifier's dispatch rule is small, deterministic, well-specified.
public class PartFileClassifierTests
{
    private static readonly PartFileClassifier Classifier = new();

    // An STL carries no machine, process or plate: nothing to slice it with, so it is refused outright.
    [Theory]
    [InlineData("bracket.stl")]
    [InlineData("BRACKET.STL")]
    public void Classify_Stl_IsRefused(string fileName)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("solid test\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid test"));

        var act = () => Classifier.Classify(stream, fileName);

        act.Should().Throw<UnsupportedPartFileFormatException>().WithMessage("STL files carry no slicing settings*");
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
    public void Classify_ProjectThreeMf_RequiresSlicing_WithItsOwnSettings()
    {
        using var stream = BuildZip(("3D/3dmodel.model", "<model><resources/><build/></model>"), ("Metadata/project_settings.config", "{}"));

        var result = Classifier.Classify(stream, "widget.3mf");

        result.Format.Should().Be(PartFileFormat.ThreeMf);
        result.RequiresSlicing.Should().BeTrue();
    }

    // Geometry only: no plate G-code and no project settings, so no machine, process or plate to follow.
    [Fact]
    public void Classify_GeometryOnlyThreeMf_IsRefused()
    {
        using var stream = BuildZip(("3D/3dmodel.model", "<model><resources/><build/></model>"), ("Metadata/slice_info.config", "<config/>"));

        var act = () => Classifier.Classify(stream, "widget.3mf");

        act.Should().Throw<UnsupportedPartFileFormatException>().WithMessage("This 3MF carries no slicing settings*");
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
